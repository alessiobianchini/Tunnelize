# Tunnelize

Tunnelize is a lightweight tunnel made of:

- a .NET WebSocket/HTTP proxy server (`Tunnelize`)
- a Node.js CLI client (`TunnelizeClient`)

It forwards remote HTTP requests to a local app through a persistent WebSocket connection.

## Features

- HTTP request forwarding over WebSocket
- Automatic reconnect on client disconnect
- Configurable log levels in CLI
- Tunnel id assignment and reuse
- Basic keepalive (`ping` / `pong`)

## CLI install

```bash
npm install -g tunnelize
```

## CLI usage

```bash
tunnelize <protocol> <port> [tunnelId]
```

Examples:

```bash
tunnelize http 8080
tunnelize https 443
tunnelize http 3000 abc123def4
```

## Environment

```bash
DEV_TUNNEL_URL=tunnelize.azurewebsites.net
```

## Local development

1. Clone the repository
2. Run server:

```bash
dotnet run --project Tunnelize/Tunnelize.csproj
```

3. Run client:

```bash
cd TunnelizeClient
pnpm install
node src/tunnelize.js http 8080
```

## License

MIT
