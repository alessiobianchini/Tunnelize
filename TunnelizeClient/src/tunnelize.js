#!/usr/bin/env node

const fs = require('fs');
const path = require('path');
const WebSocket = require('ws');
const http = require('http');
const https = require('https');
const process = require('process');
const zlib = require('zlib');
const pjson = require('../package.json');

const MAX_BUFFER_SIZE = 1024 * 1024 * 5;
const BASE_RECONNECT_DELAY_MS = 5000;
const MAX_RECONNECT_DELAY_MS = 30000;
const REQUEST_TIMEOUT_MS = 30000;
const logLevels = ['debug', 'info', 'log', 'warn', 'error', 'none'];
const configFilePath = path.join(process.env.HOME || process.env.USERPROFILE || __dirname, '.tunnelize_config.json');

let logLevel = loadLogLevel();

const shouldLog = (level) => logLevels.indexOf(level) >= logLevels.indexOf(logLevel);

const _console = console;
global.console = {
    ..._console,
    log: (message, ...optionalParams) => {
        shouldLog('log') && _console.log(message, ...optionalParams);
    },
    info: (message, ...optionalParams) => {
        shouldLog('info') && _console.info(message, ...optionalParams);
    },
    warn: (message, ...optionalParams) => {
        shouldLog('warn') && _console.warn(message, ...optionalParams);
    },
    error: (message, ...optionalParams) => {
        shouldLog('error') && _console.error(message, ...optionalParams);
    },
    debug: (message, ...optionalParams) => {
        shouldLog('debug') && _console.debug(message, ...optionalParams);
    }
};

global.logLevel = logLevel;

function setLogLevel(level) {
    if (!logLevels.includes(level)) {
        forceLog('error', `[ERROR] Invalid log level: ${level}. Valid levels are: ${logLevels.join(', ')}`);
        return;
    }

    logLevel = level;
    global.logLevel = logLevel;
    persistLogLevel(logLevel);
    forceLog('log', `Log level set to: ${logLevel}`);
}

function forceLog(level, message, ...optionalParams) {
    _console[level] && _console[level](message, ...optionalParams);
}

function persistLogLevel(level) {
    try {
        const config = { logLevel: level };
        fs.writeFileSync(configFilePath, JSON.stringify(config, null, 2));
    } catch (error) {
        _console.error('[ERROR] Failed to persist log level:', error.message);
    }
}

function loadLogLevel() {
    try {
        if (fs.existsSync(configFilePath)) {
            const config = JSON.parse(fs.readFileSync(configFilePath, 'utf8'));
            if (logLevels.includes(config.logLevel)) {
                return config.logLevel;
            }
        }
    } catch (error) {
        _console.error('[ERROR] Failed to load persisted log level:', error.message);
    }

    return 'log';
}

function connectToWebSocket(protocol, port, tunnelId = null, reconnectDelayMs = BASE_RECONNECT_DELAY_MS) {
    const host = process.env.DEV_TUNNEL_URL || 'tunnelize.azurewebsites.net';
    let activeTunnelId = tunnelId;
    let requestQueue = Promise.resolve();
    const wssUrl = activeTunnelId ? `wss://${host}/ws/${activeTunnelId}` : `wss://${host}/ws`;
    const ws = new WebSocket(wssUrl, { maxPayload: MAX_BUFFER_SIZE });

    ws.on('open', () => {
        reconnectDelayMs = BASE_RECONNECT_DELAY_MS;
        console.info('[INFO] Connection established with the proxy');
    });

    ws.on('close', () => {
        const nextDelay = Math.min(reconnectDelayMs * 2, MAX_RECONNECT_DELAY_MS);
        console.info(`[INFO] Connection closed, retrying in ${reconnectDelayMs / 1000} seconds`);
        setTimeout(() => connectToWebSocket(protocol, port, activeTunnelId, nextDelay), reconnectDelayMs);
    });

    ws.on('error', (error) => {
        console.error('[ERROR] WebSocket error:', error.message);
    });

    ws.on('message', (data) => {
        const message = data.toString('utf8');

        if (message === 'ping') {
            console.debug('[DEBUG] Ping received from proxy. Sending pong...');
            sendSafe(ws, 'pong');
            return;
        }

        if (isTunnelId(message)) {
            activeTunnelId = message;
            forceLog('log', `\nTunnel ID: ${message}\nAll routes and query parameters are forwarded at: https://${host}/${message}/*`);
            return;
        }

        requestQueue = requestQueue
            .then(async () => {
                const requestData = JSON.parse(message);
                console.debug('[DEBUG] Message received.');
                const response = await forwardRequestToLocalServer(requestData, protocol, port);
                sendSafe(ws, JSON.stringify(response));
            })
            .catch((error) => {
                const normalized = normalizeErrorResponse(error);
                console.error('[ERROR] Forward request failed:', normalized.body);
                sendSafe(ws, JSON.stringify(normalized));
            });
    });
}

function sendSafe(ws, payload) {
    if (ws.readyState === WebSocket.OPEN) {
        ws.send(payload, { fin: true });
        return;
    }

    console.warn('[WARN] WebSocket not open. Response dropped.');
}

async function forwardRequestToLocalServer(requestData, inputProtocol, inputPort) {
    const targetProtocol = inputProtocol && inputProtocol.toLowerCase() === 'http' ? http : https;
    const method = `${requestData.Method || 'GET'}`.toUpperCase();
    const requestPath = encodeURI(`${requestData.Route || '/'}${requestData.QueryString || ''}`);
    const filteredHeaders = filterRequestHeaders(requestData.Headers || {});

    const options = {
        hostname: 'localhost',
        port: inputPort || 8080,
        method,
        headers: filteredHeaders,
        timeout: REQUEST_TIMEOUT_MS,
        path: requestPath,
        rejectUnauthorized: false
    };

    return new Promise((resolve, reject) => {
        const req = targetProtocol.request(options, async (res) => {
            const responseChunks = [];

            res.on('data', (chunk) => {
                responseChunks.push(chunk);
            });

            res.on('error', (error) => {
                reject(createGatewayError(error.message));
            });

            res.on('end', async () => {
                try {
                    const bodyBuffer = Buffer.concat(responseChunks);
                    const decodedBody = await decodeResponseBody(bodyBuffer, res.headers['content-encoding']);
                    const headers = normalizeResponseHeaders(res.headers);

                    delete headers['content-encoding'];
                    delete headers['content-length'];

                    resolve({
                        statusCode: res.statusCode || 500,
                        body: decodedBody.toString('utf8'),
                        contentType: res.headers['content-type'] || 'application/json',
                        headers
                    });
                } catch (error) {
                    reject(createGatewayError(error.message));
                }
            });
        });

        req.on('error', (error) => {
            reject(createGatewayError(error.message));
        });

        req.on('timeout', () => {
            req.destroy(new Error('Request to local server timed out.'));
        });

        if (canHaveBody(method) && requestData.Body) {
            req.write(requestData.Body);
        }

        req.end();
    });
}

function filterRequestHeaders(headers) {
    const blockedHeaders = new Set([
        'connection',
        'host',
        'upgrade',
        'sec-websocket-key',
        'sec-websocket-version',
        'sec-websocket-extensions'
    ]);

    const filtered = {};
    for (const [key, value] of Object.entries(headers)) {
        if (!blockedHeaders.has(key.toLowerCase())) {
            filtered[key] = value;
        }
    }

    return filtered;
}

function normalizeResponseHeaders(headers) {
    const blockedHeaders = new Set([
        'connection',
        'keep-alive',
        'proxy-authenticate',
        'proxy-authorization',
        'te',
        'trailer',
        'transfer-encoding',
        'upgrade',
        'content-encoding',
        'content-length'
    ]);

    const normalized = {};

    for (const [key, value] of Object.entries(headers)) {
        if (value === undefined) {
            continue;
        }

        const normalizedKey = key.toLowerCase();
        if (blockedHeaders.has(normalizedKey)) {
            continue;
        }

        normalized[normalizedKey] = Array.isArray(value) ? value.join(', ') : `${value}`;
    }

    return normalized;
}

function decodeResponseBody(buffer, encoding) {
    return new Promise((resolve, reject) => {
        if (!encoding) {
            resolve(buffer);
            return;
        }

        const normalizedEncoding = `${encoding}`.toLowerCase();

        if (normalizedEncoding.includes('br')) {
            zlib.brotliDecompress(buffer, (error, decoded) => (error ? reject(error) : resolve(decoded)));
            return;
        }

        if (normalizedEncoding.includes('gzip')) {
            zlib.gunzip(buffer, (error, decoded) => (error ? reject(error) : resolve(decoded)));
            return;
        }

        if (normalizedEncoding.includes('deflate')) {
            zlib.inflate(buffer, (error, decoded) => (error ? reject(error) : resolve(decoded)));
            return;
        }

        resolve(buffer);
    });
}

function createGatewayError(message) {
    return {
        statusCode: 502,
        body: message,
        contentType: 'text/plain',
        headers: {}
    };
}

function normalizeErrorResponse(error) {
    if (error && typeof error === 'object' && Number.isInteger(error.statusCode) && typeof error.body === 'string') {
        return {
            statusCode: error.statusCode,
            body: error.body,
            contentType: error.contentType || 'text/plain',
            headers: error.headers || {}
        };
    }

    return createGatewayError(error?.message || 'Unexpected client forwarding error.');
}

function isTunnelId(message) {
    return /^[0-9A-Za-z]{10}$/.test(message);
}

function canHaveBody(method) {
    return ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method);
}

function startTunnelize(protocol, port, tunnelId) {
    connectToWebSocket(protocol, port, tunnelId);
}

function showHelp() {
    forceLog('log', '\nUsage: tunnelize <protocol> <port> [tunnelId]\n\nOptions:\n  protocol  Either "http" or "https"\n  port      The port number to connect to on localhost\n\nExamples:\n  tunnelize http 8080\n  tunnelize https 443\n  tunnelize loglevel debug\n');
}

module.exports = { startTunnelize, setLogLevel };

if (require.main === module) {
    const args = process.argv.slice(2);

    if (args.length === 1 && args[0] === 'help') {
        showHelp();
        process.exit(0);
    }

    if (args.length === 1 && args[0] === 'version') {
        console.log(`Tunnelize version: ${pjson.version}`);
        process.exit(0);
    }

    if (args.length === 2 && args[0] === 'loglevel') {
        setLogLevel(args[1]);
        process.exit(0);
    }

    if (args.length < 2) {
        console.error('[ERROR] Usage: tunnelize <protocol> <port> [tunnelId]');
        showHelp();
        process.exit(1);
    }

    const [protocol, rawPort, tunnelId] = args;

    if (!['http', 'https'].includes(protocol)) {
        console.error('[ERROR] Protocol must be either "http" or "https"');
        process.exit(1);
    }

    const port = Number.parseInt(rawPort, 10);
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
        console.error('[ERROR] Port must be a valid integer between 1 and 65535');
        process.exit(1);
    }

    forceLog('log', `Starting tunnelize with protocol: ${protocol}, port: ${port}, log level: ${logLevel}`);
    startTunnelize(protocol, port, tunnelId);
}
