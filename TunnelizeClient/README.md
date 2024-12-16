# Tunnelize

A CLI tool to create a WebSocket tunnel for forwarding HTTP/HTTPS requests to a local server.

## Overview

Tunnelize helps developers quickly create a tunnel to expose their local development server. It's lightweight, fast, and easy to use, making it ideal for testing APIs, webhooks, or local applications.

## Installation

Install globally using npm:

```bash
npm install -g tunnelize
```

## Usage

Run the CLI tool by providing the host and port:

```bash
tunnelize <host> <port>
```

Example:

```bash
tunnelize localhost 8080
```

### Default Behavior:
- Assumes HTTP for the protocol unless specified.
- For HTTPS:
  ```bash
  tunnelize https localhost 8080
  ```

## More Information

For additional documentation, examples, and source code, visit the [Tunnelize GitHub repository](https://github.com/alessiobianchini/Tunnelize).

---

## License

This project is licensed under the MIT License.

---

## Author

Developed by [Alessio Bianchini](https://github.com/alessiobianchini).
