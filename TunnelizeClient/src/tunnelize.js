#!/usr/bin/env node

const WebSocket = require('ws');
const http = require('http');
const https = require('https');
const process = require('process');
const zlib = require('zlib');

//Local development
process.env["NODE_TLS_REJECT_UNAUTHORIZED"] = 0;

function connectToWebSocket(protocol, port, tunnelId = null) {
    const url = process.env.DEV_TUNNEL_URL || 'tunnelize.azurewebsites.net';
    const MAX_BUFFER_SIZE = 1024 * 1024 * 5;
    let wssUrl = !!tunnelId ? `wss://${url}/ws/${tunnelId}` : `wss://${url}/ws`;
    const ws = new WebSocket(wssUrl, {maxPayload: MAX_BUFFER_SIZE});

    ws.on('open', () => {
        console.log('[INFO] Connection established with the proxy');
    });

    ws.on('close', () => {
        console.log('[INFO] Connection closed, retrying in 5 seconds');
        setTimeout(() => connectToWebSocket(protocol, port), 5000);
    });

    ws.on('error', (error) => {
        console.error('[ERROR] WebSocket error:', error.message);
    });

    ws.on('message', (data) => {
        const message = data.toString('utf8');

        if (isTunnelId(message)) {
            console.log(`\n✅ Tunnel ID received: ${message}\nYou can use now https://${url}/${message}/*?param=abc`);
        } else {
            try {
                const requestData = JSON.parse(message);
                console.log('[INFO] Message received.');
                forwardRequestToLocalServer(requestData, protocol, port)
                    .then(response => {
                        console.log(`[DEBUG] Sending back response...`);
                        ws.send(JSON.stringify(response), { fin: true });
                    })
                    .catch(error => {
                        console.error('[ERROR] Forward request failed:', error);
                        ws.send(JSON.stringify(error), { fin: true });
                    });
            } catch (error) {
                console.error('[FATAL ERROR] Unexpected exception:', error.message);
                ws.send(JSON.stringify({ statusCode: 500, message: error.message }));
            }
        }
    });
}

function forwardRequestToLocalServer(requestData, inputProtocol, inputPort) {
    return new Promise((resolve, reject) => {
        const path = encodeURI(requestData.Route + requestData.QueryString);
        const protocol = inputProtocol && inputProtocol.toLowerCase() === 'http' ? http : https;
        const options = {
            hostname: 'localhost',
            port: inputPort || 8080,
            method: requestData.Method,
            headers: requestData.Headers,
            timeout: 30000,
            path: path,
            rejectUnauthorized: false
        };

        const req = protocol.request(options, (res) => {
            console.log(`[INFO] Status Code: ${res.statusCode}`);
            
            let responseData = [];
            res.on('data', (chunk) => {
                responseData.push(chunk);
            });

            res.on('end', () => {
                responseData = Buffer.concat(responseData);
                const contentEncoding = res.headers['content-encoding'];

                if (contentEncoding === 'br') {
                    zlib.brotliDecompress(responseData, (err, decompressed) => {
                        if (err) {
                            console.error('[ERROR] Brotli decompression failed:', err);
                            return reject({ statusCode: 500, message: 'Brotli decompression failed' });
                        }
                        handleDecodedResponse(res.statusCode, decompressed.toString(), resolve, reject);
                    });
                } else {
                    handleDecodedResponse(res.statusCode, responseData.toString(), resolve, reject);
                }
            });

            res.on('error', (e) => {
                console.error(`[ERROR] Response stream error: ${e.message}`);
                reject({ statusCode: 500, message: e.message });
            });
        });

        req.on('error', (e) => {
            console.error(`[ERROR] Request error: ${e.message}`);
            reject({ statusCode: 500, message: e.message });
        });

        if (requestData.Method === 'POST' && requestData.Body) {
            req.write(requestData.Body);
        }

        req.end();
    });
}

function isTunnelId(message) {
    const tunnelIdPattern = /^[0-9A-Za-z]{10}$/;
    return tunnelIdPattern.test(message);
}

function startTunnelize(protocol, port) {
    connectToWebSocket(protocol, port);
}

function showHelp() {
    console.log(`\nUsage: tunnelize <protocol> <port>\n\nOptions:\n  protocol  Either "http" or "https" (default is http)\n  port      The port number to connect to on localhost (default is 8080)\n\nExamples:\n  tunnelize http 8080\n  tunnelize https 443\n`);
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

function handleDecodedResponse(statusCode, body, resolve, reject) {
    console.log(`[DEBUG] Decoded response: ${body}`);

    const response = {
        statusCode: statusCode,
        body: body
    };

    if (statusCode === 404) {
        console.error('[ERROR] Resource not found (404)');
        reject(response);
    } else if (statusCode >= 400) {
        console.error(`[ERROR] HTTP error: ${statusCode}, Body: ${body}`);
        reject(response);
    } else {
        console.log(`[INFO] Request successful with status code: ${statusCode}`);
        resolve(response);
    }
}
