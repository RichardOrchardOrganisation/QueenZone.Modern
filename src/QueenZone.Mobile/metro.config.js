const path = require('node:path');
const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);
// The website and app import this same dependency-free solving engine.
config.watchFolders = [...(config.watchFolders ?? []), path.resolve(__dirname, '../QueenZone.Web/wwwroot/js')];
// Babel helpers for that external source resolve from this app's dependencies.
config.resolver.nodeModulesPaths = [...(config.resolver.nodeModulesPaths ?? []), path.resolve(__dirname, 'node_modules')];

module.exports = config;
