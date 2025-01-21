# Tunnelize

Tunnelize is a command-line tool that allows you to easily forward HTTP or HTTPS requests from a remote server to a local instance. It is designed to facilitate secure, simple, and reliable tunnel connections for development environments.

## 🚀 Features
- **WebSocket Connection**: Establish a persistent connection with a remote proxy server.
- **HTTP/HTTPS Protocol**: Supports tunneling for both HTTP and HTTPS requests.
- **Automatic Reconnection**: If the connection is dropped, Tunnelize automatically retries.
- **Simple CLI**: Intuitive command-line interface for fast setup and usage.
- **Configurable Log Levels**: Dynamically adjust logging levels (debug, info, log, warn, error) for better control.
- **Persistent Log Levels**: The log level persists across sessions.

## 📦 Installation

To install Tunnelize globally as an NPM package, run:

```bash
npm install -g tunnelize
```

Once installed, you can use the `tunnelize` command from anywhere.

## 🔧 Usage

To start Tunnelize, use the following command:

```bash
tunnelize <protocol> <port>
```

**Options:**

| Parameter  | Description                     | Default |
|------------|---------------------------------|---------|
| `protocol` | Either `http` or `https`         | `http`  |
| `port`     | Port number to connect on localhost | 8080   |

### Examples

1. **Start a tunnel for HTTP on port 8080**
   ```bash
   tunnelize http 8080
   ```

2. **Start a tunnel for HTTPS on port 443**
   ```bash
   tunnelize https 443
   ```

3. **Set log level to debug**
   ```bash
   tunnelize loglevel debug
   ```

4. **Show help information**
   ```bash
   tunnelize help
   ```

## 🔐 Environment Variables

You can customize the remote proxy server URL using environment variables.

```bash
export DEV_TUNNEL_URL='your-proxy-url-here'
```

## 📘 Commands

| Command        | Description                        |
|----------------|-----------------------------------|
| `tunnelize http 8080` | Start a tunnel using HTTP on port 8080 |
| `tunnelize https 443`  | Start a tunnel using HTTPS on port 443  |
| `tunnelize loglevel <level>` | Set the log level dynamically (debug, info, log, warn, error) |
| `tunnelize help`       | Show available commands and usage info  |

## 💻 Development

To contribute or develop locally, follow these steps:

1. Clone the repository:
   ```bash
   git clone https://github.com/alessiobianchini/Tunnelize.git
   cd Tunnelize
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Link the package locally for testing:
   ```bash
   npm link
   ```

4. Run the CLI locally:
   ```bash
   tunnelize http 8080
   ```

## 🤝 Contributing

Contributions are welcome! To contribute, please:

1. Fork the repository.
2. Create a new branch for your feature or bugfix.
3. Submit a pull request with your changes.

## 🔖 License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for more information.

## 📧 Contact

For questions, issues, or feature requests, feel free to open an issue on the [GitHub repository](https://github.com/alessiobianchini/Tunnelize) or contact the project maintainer via email: alessio.bianchini@doit.it

---

> **Note:** This tool is intended for development purposes only and should not be used in production environments without proper security measures.
