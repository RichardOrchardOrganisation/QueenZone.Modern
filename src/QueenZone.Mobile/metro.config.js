const path = require('node:path');
const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);
// The website and app import this same dependency-free solving engine.
config.watchFolders = [...(config.watchFolders ?? []), path.resolve(__dirname, '../QueenZone.Web/wwwroot/js')];

module.exports = config;
