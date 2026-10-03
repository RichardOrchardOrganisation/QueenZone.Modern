const { createTransformer } = require('babel-jest');
const { resolveBabelOptions } = require('jest-expo/src/resolveBabelOptions');
const transformer = createTransformer(resolveBabelOptions(process.cwd()));

module.exports = {
  ...transformer,
  process(source, filename, options) {
    // Expo replaces a 'widget' function with a serialized string. For this
    // Jest-only transform, execute and instrument the original function body.
    // Production serialization and the source-level import-freedom guard stay intact.
    return transformer.process(source.replace("'widget';", "'widget-test';"), filename, options);
  },
};
