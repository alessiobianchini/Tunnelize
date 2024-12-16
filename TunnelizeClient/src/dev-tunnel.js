#!/usr/bin/env node

const WebSocket = require('ws');
const http = require('http');
const https = require('https');
const process = require('process');

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

function connectToWebSocket() {
    const url = process.env.DEV_TUNNEL_URL;
    const ws = new WebSocket(`wss://${url}/ws`);

    ws.on('open', () => {
        console.log('[INFO] Connection established with the proxy');
    });

    ws.on('close', () => {
        console.log('[INFO] Connection closed, retrying in 5 seconds');
        setTimeout(connectToWebSocket, 5000);
    });

    ws.on('error', (error) => {
        console.error('[ERROR] WebSocket error:', error.message);
    });

    ws.on('message', (data) => {
        const message = data.toString('utf8');

        if (isTunnelId(message)) {
            console.log(`✅ Tunnel ID received: ${message}`);
        } else {
            try {
                const requestData = JSON.parse(message);
                console.log('[INFO] Message received:', message);
                forwardRequestToLocalServer(requestData);
            } catch (error) {
                console.error('[ERROR] Failed to parse message as JSON:', error.message);
            }
        }
    });
}

function forwardRequestToLocalServer(requestData) {
    const path = encodeURI(requestData.Route + requestData.QueryString);
    const protocol = requestData.Protocol && requestData.Protocol.toLowerCase() === 'http' ? http : https;
    const options = {
        hostname: 'localhost',
        port: requestData.Port || 58275,
        method: requestData.Method,
        headers: requestData.Headers,
        timeout: 30000,
        path: path
    };

    const req = protocol.request(options, (res) => {
        console.log(`[INFO] Status Code: ${res.statusCode}`);

        let responseData = '';
        res.setEncoding('utf8');

        res.on('data', (chunk) => {
            responseData += chunk;
        });

        res.on('end', () => {
            console.log(`[INFO] Full response: ${responseData}`);

            if (res.statusCode === 404) {
                console.error('[ERROR] Resource not found (404)');
            } else if (res.statusCode >= 400) {
                console.error(`[ERROR] HTTP error: ${res.statusCode}, Body: ${responseData}`);
            } else {
                console.log(`[INFO] Request successful with status code: ${res.statusCode}`);
            }
        });

        res.on('error', (e) => {
            console.error(`[ERROR] Response stream error: ${e.message}`);
        });
    });

    req.on('error', (e) => {
        console.error(`[ERROR] Request error: ${e.message}`);
    });

    if (requestData.Method === 'POST' && requestData.Body) {
        req.write(requestData.Body);
    }
    req.end();
}

function isTunnelId(message) {
    const tunnelIdPattern = /^[0-9A-Za-z]{10}$/;
    return tunnelIdPattern.test(message);
}

function startTunnelize(protocol, port) {
    const hostname = `${protocol}://localhost`;
    connectToWebSocket(hostname, port);
}

function showHelp() {
    console.log(`
Usage: tunnelize <protocol> <port>

Options:
  protocol  Either "http" or "https" (default is http)
  port      The port number to connect to on localhost (default is 58275)

Examples:
  tunnelize http 58275
  tunnelize https 443

`);
}

module.exports = { startTunnelize };

if (require.main === module) {
    const args = process.argv.slice(2);

    if (args.length === 1 && args[0] === 'help') {
        showHelp();
        process.exit(0);
    }

    if (args.length < 2) {
        console.error('[ERROR] Usage: tunnelize <protocol> <port>');
        showHelp();
        process.exit(1);
    }

    const [protocol, port] = args;

    if (!['http', 'https'].includes(protocol)) {
        console.error('[ERROR] Protocol must be either "http" or "https"');
        process.exit(1);
    }

    console.log(`[INFO] Starting tunnelize with protocol: ${protocol} and port: ${port}`);
    startTunnelize(protocol, port);
}