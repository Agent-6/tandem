const fs = require('fs');
const path = require('path');

// Read environment variables
const apiUrl = process.env.API_HTTP || 'http://localhost:5072';

const devEnvContent = `export const environment = {
  production: false,
  apiUrl: '${apiUrl}/api',
  hubUrl: '${apiUrl}/hubs/document',
};
`;

const prodEnvContent = `export const environment = {
  production: true,
  apiUrl: '${apiUrl}/api',
  hubUrl: '${apiUrl}/hubs/document',
};
`;

const devEnvPath = path.join(__dirname, 'src', 'environments', 'environment.ts');
const prodEnvPath = path.join(__dirname, 'src', 'environments', 'environment.prod.ts');

fs.writeFileSync(devEnvPath, devEnvContent, 'utf8');
fs.writeFileSync(prodEnvPath, prodEnvContent, 'utf8');
console.log(`Environment configured with API URL: ${apiUrl}`);
