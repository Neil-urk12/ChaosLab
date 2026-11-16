const { getDefaultConfig } = require('expo/metro-config');
const path = require('path');

const config = getDefaultConfig(__dirname);

// The web app's response types and pure helpers (labels, formatting, scoring) are shared, not copied:
// `@web/*` in tsconfig.json points at this folder.
config.watchFolders = [path.resolve(__dirname, '../Frontend/src/lib')];

module.exports = config;
