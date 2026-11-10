# fan_studio deployment

The public site is served by one standard-library Python process per role:

| Role | Port | Content |
|---|---:|---|
| `site` | 8180 | `site/` |
| `api` | 8181 | `/v1/health`, `/v1/latest`, `/v1/motors`, `/v1/scores`, `/v1/links` |
| `download` | 8182 | GitHub Releases redirect |

Run locally:

```bash
python3 server/fan_studio_web.py --role site --port 8180
python3 server/fan_studio_web.py --role api --port 8181
python3 server/fan_studio_web.py --role download --port 8182
```

The Cloudflare Tunnel ingress should route:

```text
fanstudio.bony-doorframe-shortly.top  -> http://127.0.0.1:8180
fan-api.bony-doorframe-shortly.top    -> http://127.0.0.1:8181
fan-download.bony-doorframe-shortly.top -> http://127.0.0.1:8182
```

Install the user services:

```bash
mkdir -p ~/.config/systemd/user
cp deploy/systemd/fan-*.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now fan-studio-site.service fan-api.service fan-download-redirect.service
```

No API key is required by the public service. It only reads local project
metadata and the public GitHub Releases API.
