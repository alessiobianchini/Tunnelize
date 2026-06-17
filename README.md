# Tunnelize

Tunnelize is a lightweight, bidirectional tunnel system designed to forward remote HTTP requests to a local application through a persistent WebSocket connection.

It consists of two primary components:
- A **.NET** WebSocket/HTTP proxy server (`Tunnelize`)
- A **Node.js** CLI client (`TunnelizeClient`)

## Features

- **HTTP Forwarding**: Seamlessly forward HTTP requests over a secure WebSocket tunnel.
- **Auto-Reconnect**: The client automatically attempts to reconnect upon disconnection.
- **Tunnel Management**: Deterministic tunnel ID assignment and reuse.
- **Keepalive**: Built-in ping/pong mechanism to prevent idle connection drops.
- **Configurable Logging**: Adjustable log levels within the CLI.

## Installation

To install the Node.js CLI client globally, run:

```bash
npm install -g tunnelize
```

*Note: Alternatively, you can use `pnpm` or `yarn`.*

## Usage

Start the tunnel by specifying the local protocol and port:

```bash
tunnelize <protocol> <port> [tunnelId]
```

### Examples

```bash
# Forward remote traffic to a local HTTP server on port 8080
tunnelize http 8080

# Forward remote traffic to a local HTTPS server on port 443
tunnelize https 443

# Forward traffic to port 3000 and request a specific custom tunnel ID
tunnelize http 3000 abc123def4
```

## Environment Variables

You can configure the remote server URL using environment variables:

```bash
DEV_TUNNEL_URL=tunnelize.azurewebsites.net
```

## Local Development

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/)
- [pnpm](https://pnpm.io/)

### Running the Server

Navigate to the project root and run the .NET server:

```bash
dotnet run --project Tunnelize/Tunnelize.csproj
```

### Running the Client

Open a new terminal, navigate to the client directory, install dependencies, and start the client:

```bash
cd TunnelizeClient
pnpm install
node src/tunnelize.js http 8080
```

## Troubleshooting / FAQ

### The CLI client fails to connect to the server
**Solution:** Ensure that the server is running and accessible. If running locally, verify that `DEV_TUNNEL_URL` is either unset or pointing to your local server port (e.g., `localhost:5000`). If pointing to Azure, ensure the App Service is active and WebSocket support is enabled in the Azure portal.

### Requests are dropping or timing out
**Solution:** Check the keepalive intervals. The .NET server expects a `KeepAliveInterval` (default 30s). Ensure the Node.js client is properly responding to `ping` frames with `pong` frames. Check the CLI logs for disconnection events.

### "Expected a WebSocket request" Error
**Solution:** This occurs if you try to hit the `/ws` endpoint with a standard HTTP GET request. The `/ws` path is strictly reserved for WebSocket upgrade requests from the `TunnelizeClient`.

### Custom Tunnel ID is not being accepted
**Solution:** Tunnel IDs must be exactly 10 alphanumeric characters. If an invalid ID is provided, the server will automatically generate and assign a random valid 10-character ID.

## License

This project is licensed under the [MIT License](LICENSE).
