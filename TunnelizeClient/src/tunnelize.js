#!/usr/bin/env node

const fs = require('fs');
const path = require('path');
const WebSocket = require('ws');
const http = require('http');
const https = require('https');
const process = require('process');
const zlib = require('zlib');
const pjson = require('../package.json');

// Local development
// process.env["NODE_TLS_REJECT_UNAUTHORIZED"] = 0;

const logLevels = ["debug", "info", "log", "warn", "error", "none"];
const configFilePath = path.resolve(__dirname, '.tunnelize_config.json');

let logLevel = loadLogLevel();

const shouldLog = (level) => {
    return logLevels.indexOf(level) >= logLevels.indexOf(logLevel);
};

const _console = console;
global.console = {
    ..._console,
    log: (message, ...optionalParams) => {
        shouldLog("log") && _console.log(message, ...optionalParams);
    },
    info: (message, ...optionalParams) => {
        shouldLog("info") && _console.info(message, ...optionalParams);
    },
    warn: (message, ...optionalParams) => {
        shouldLog("warn") && _console.warn(message, ...optionalParams);
    },
    error: (message, ...optionalParams) => {
        shouldLog("error") && _console.error(message, ...optionalParams);
    },
    debug: (message, ...optionalParams) => {
        shouldLog("debug") && _console.debug(message, ...optionalParams);
    },
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
    return "log";
}

function connectToWebSocket(protocol, port, tunnelId = null) {
    const url = process.env.DEV_TUNNEL_URL || 'tunnelize.azurewebsites.net';
    const MAX_BUFFER_SIZE = 1024 * 1024 * 5;
    let wssUrl = !!tunnelId ? `wss://${url}/ws/${tunnelId}` : `wss://${url}/ws`;
    const ws = new WebSocket(wssUrl, { maxPayload: MAX_BUFFER_SIZE });

    ws.on('open', () => {
        console.info('[INFO] Connection established with the proxy');
    });

    ws.on('close', () => {
        console.info('[INFO] Connection closed, retrying in 5 seconds');
        setTimeout(() => connectToWebSocket(protocol, port, tunnelId), 5000);
    });

    ws.on('error', (error) => {
        console.error('[ERROR] WebSocket error:', error.message);
    });

    ws.on('message', (data) => {
        const message = data.toString('utf8');

        if (message === 'ping') {
            console.debug('[DEBUG] Ping received from proxy. Sending pong...');
            ws.send('pong', { fin: true });
            return;
        }

        if (isTunnelId(message)) {
            forceLog('log', `\n✅ Tunnel ID received: ${message}\nAll routes and query parameters are forwarded. Access it here: https://${url}/${message}/*`);
        } else {
            try {
                const requestData = JSON.parse(message);
                console.debug('[DEBUG] Message received.');
                forwardRequestToLocalServer(requestData, protocol, port)
                    .then(response => {
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
            console.debug(`[DEBUG] Status Code: ${res.statusCode}`);

            let responseData = [];
            res.on('data', (chunk) => {
                responseData.push(chunk);
            });

            res.on('end', () => {
                responseData = Buffer.concat(responseData);
                const contentEncoding = res.headers['content-encoding'];
                const contentType = res.headers['content-type'] || 'application/json';

                if (contentEncoding === 'br') {
                    zlib.brotliDecompress(responseData, (err, decompressed) => {
                        if (err) {
                            console.error('[ERROR] Brotli decompression failed:', err);
                            return reject({ statusCode: 500, message: 'Brotli decompression failed' });
                        }
                        handleDecodedResponse(res.statusCode, decompressed.toString(), contentType, resolve, reject);
                    });
                } else {
                    handleDecodedResponse(res.statusCode, responseData.toString(), contentType, resolve, reject);
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

function startTunnelize(protocol, port, tunnelId) {
    connectToWebSocket(protocol, port, tunnelId);
}

function showHelp() {
    forceLog('log', `\nUsage: tunnelize <protocol> <port> [tunnelId] <logLevel>\n\nOptions:\n  protocol  Either "http" or "https" (default is http)\n  port      The port number to connect to on localhost (default is 8080)\n\nExamples:\n  tunnelize http 8080\n  tunnelize https 443\n  tunnelize loglevel debug\n`);
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

    const [protocol, port, tunnelId] = args;

    if (!['http', 'https'].includes(protocol)) {
        console.error('[ERROR] Protocol must be either "http" or "https"');
        process.exit(1);
    }

    forceLog('log', `Starting tunnelize with protocol: ${protocol}, port: ${port}, log level: ${logLevel}`);
    startTunnelize(protocol, port, tunnelId);
}

function handleDecodedResponse(statusCode, body, contentType, resolve, reject) {
    const response = {
        statusCode: statusCode,
        body: body,
        contentType: contentType
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
