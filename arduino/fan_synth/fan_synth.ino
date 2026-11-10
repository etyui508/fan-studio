/*
 * fan_synth.ino — 让 4-pin 风扇跟着乐谱唱歌
 *
 * 模式 A (主要用法, 安全): 25kHz PWM 打风扇的 PWM 线 -> 改转速 -> 叶片通过频率就是音高
 * 模式 B (实验, 需要 MOSFET): 音频方波斩波 12V -> 电机线圈自己啸叫出旋律
 *
 * 串口 115200, 每行一条命令:
 *   d<0..1000>   模式 A: 占空比千分比            -> "ok d<v>"
 *   a<Hz>        模式 B: 音频方波频率, 0 = 关     -> "ok a<Hz>"
 *   q            查询                          -> "t<rpm> d<permille> a<hz>"
 *   x            全部停
 *
 * 失败保护: 4 秒收不到任何命令就把占空比降到 0(主机崩了风扇不该卡在高转速)。
 *
 * 接线 (Arduino Uno / Nano, 5V 板):
 *   风扇 12V   -> 外部 12V 电源 +,  GND 与 Arduino GND 共地
 *   风扇 GND   -> 外部 12V 电源 -
 *   风扇 PWM   -> D9   (OC1A, 25kHz)
 *   风扇 TACH  -> D2   (INT0, 加上拉; 很多风扇内部已有上拉, 不放心就外接 10k 到 5V)
 *   模式 B: D11 -> 100R -> MOSFET 栅极, MOSFET 串在风扇 GND 侧, 加续流二极管
 *
 * 注意: 3-pin 风扇没有 PWM 线, 只能走模式 B。
 */

const uint8_t PIN_FAN_PWM = 9;   // OC1A
const uint8_t PIN_TACH    = 2;   // INT0
const uint8_t PIN_AUDIO   = 11;  // OC2A, 由 tone() 驱动

const uint16_t PWM_TOP = 639;    // 16MHz / (639+1) = 25.0 kHz

volatile uint32_t tachLastUs   = 0;
volatile uint32_t tachPeriodUs = 0;
volatile uint32_t tachRpm      = 0;

volatile int      gDutyPermille = 0;
volatile uint16_t gToneHz       = 0;
uint32_t lastCmdMs = 0;
bool     tripped   = false;

void tachISR() {
  uint32_t now = micros();
  uint32_t d = now - tachLastUs;
  tachLastUs = now;
  // 每转 2 个脉冲; d 在 [100us, 200ms] 之间才算「30~60000rpm」
  if (d > 100UL && d < 200000UL) {
    tachPeriodUs = d;
    tachRpm = 30000000UL / d;
  }
}

void setupPwm() {
  pinMode(PIN_FAN_PWM, OUTPUT);
  // Fast PWM, mode 14: TOP = ICR1, OC1A 非反相
  TCCR1A = _BV(COM1A1) | _BV(WGM11);
  TCCR1B = _BV(WGM13) | _BV(WGM12) | _BV(CS10);
  TCNT1  = 0;
  ICR1   = PWM_TOP;
  OCR1A  = 0;
}

void setDuty(int permille) {
  permille = constrain(permille, 0, 1000);
  gDutyPermille = permille;
  if (permille == 0) {                 // 完全断开, 免得卡在 1% 抽风
    TCCR1A &= ~_BV(COM1A1);
    digitalWrite(PIN_FAN_PWM, LOW);
    return;
  }
  TCCR1A |= _BV(COM1A1);
  uint32_t ocr = (uint32_t)(PWM_TOP + 1) * (uint32_t)permille / 1000UL;
  if (ocr > PWM_TOP) ocr = PWM_TOP;
  OCR1A = (uint16_t)ocr;
}

void setTone(long hz) {
  if (hz <= 0) {
    noTone(PIN_AUDIO);
    gToneHz = 0;
    return;
  }
  if (hz > 4000) hz = 4000;
  gToneHz = (uint16_t)hz;
  tone(PIN_AUDIO, (unsigned int)hz);   // 50% 方波
}

void report() {
  noInterrupts();
  uint32_t rpm = tachRpm;
  interrupts();
  Serial.print(F("t"));
  Serial.print(rpm);
  Serial.print(F(" d"));
  Serial.print((int)gDutyPermille);
  Serial.print(F(" a"));
  Serial.println((int)gToneHz);
}

void handleLine(char *s) {
  if (!s[0]) return;
  lastCmdMs = millis();
  tripped = false;
  long v = atol(s + 1);
  switch (s[0]) {
    case 'd': setDuty((int)v); Serial.print(F("ok d")); Serial.println((int)v);  break;
    case 'a': setTone(v);      Serial.print(F("ok a")); Serial.println(v);       break;
    case 'q': report();                                                          break;
    case 'x': setDuty(0); setTone(0); Serial.println(F("ok x"));                 break;
    default:  Serial.println(F("err ?"));                                        break;
  }
}

char    buf[24];
uint8_t bufLen = 0;

void setup() {
  Serial.begin(115200);
  setupPwm();
  pinMode(PIN_TACH, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(PIN_TACH), tachISR, FALLING);
  setDuty(0);
  setTone(0);
  lastCmdMs = millis();
  Serial.println(F("fan_synth ready"));
}

void loop() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      buf[bufLen] = 0;
      handleLine(buf);
      bufLen = 0;
    } else if (bufLen < sizeof(buf) - 1) {
      buf[bufLen++] = c;
    }
  }

  // 主机失联保护
  if (!tripped && (millis() - lastCmdMs > 4000UL)) {
    setDuty(0);
    setTone(0);
    tripped = true;
    Serial.println(F("failsafe: host silent, fan stopped"));
  }
}
