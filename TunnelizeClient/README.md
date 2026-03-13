# Tunnelize CLI

CLI that opens a WebSocket tunnel and forwards remote HTTP requests to your local app.

## Install

```bash
npm install -g tunnelize
```

## Usage

```bash
tunnelize <protocol> <port> [tunnelId]
```

- `protocol`: `http` or `https`
- `port`: local port to forward to (1-65535)
- `tunnelId` (optional): reuse an existing tunnel id

Examples:

```bash
tunnelize http 8080
tunnelize https 443
tunnelize http 3000 abc123def4
```

## Other commands

```bash
tunnelize help
tunnelize version
tunnelize loglevel debug
```

Supported log levels: `debug`, `info`, `log`, `warn`, `error`, `none`.

## Configuration

- `DEV_TUNNEL_URL`: override the default proxy host (`tunnelize.azurewebsites.net`)
- Log level is persisted in `~/.tunnelize_config.json`
