const React = require('react');
const { View } = require('react-native');

const recorded = {
  pinch: null,
  pan: null,
  zoomPan: null,
  tap: null,
  singleTap: null,
};

function chain(kind) {
  const handlers = {};
  const config = {};
  const gesture = {
    onBegin(fn) {
      handlers.onBegin = fn;
      return gesture;
    },
    onUpdate(fn) {
      handlers.onUpdate = fn;
      return gesture;
    },
    onEnd(fn) {
      handlers.onEnd = fn;
      return gesture;
    },
    onTouchesDown(fn) {
      handlers.onTouchesDown = fn;
      return gesture;
    },
    onTouchesMove(fn) {
      handlers.onTouchesMove = fn;
      return gesture;
    },
    manualActivation() {
      return gesture;
    },
    numberOfTaps(value) {
      config.numberOfTaps = value;
      return gesture;
    },
    maxDuration(value) {
      config.maxDuration = value;
      return gesture;
    },
    maxDistance(value) {
      config.maxDistance = value;
      return gesture;
    },
    maxPointers() {
      return gesture;
    },
    activeOffsetX() {
      return gesture;
    },
    failOffsetY() {
      return gesture;
    },
    runOnJS(value) {
      config.runOnJS = value;
      return gesture;
    },
    handlers,
    config,
  };
  recorded[kind] = gesture;
  return gesture;
}

const passthrough = ({ children }) => React.createElement(View, null, children);

module.exports = {
  GestureHandlerRootView: passthrough,
  GestureDetector: passthrough,
  Gesture: {
    Native: () => chain('native'),
    Pinch: () => {
      recorded.pan = null;
      recorded.zoomPan = null;
      recorded.tap = null;
      recorded.singleTap = null;
      return chain('pinch');
    },
    Pan: () => chain(recorded.pan == null ? 'pan' : 'zoomPan'),
    Tap: () => chain(recorded.tap == null ? 'tap' : 'singleTap'),
    Simultaneous: (...gestures) => gestures[0],
    Exclusive: (...gestures) => gestures[0],
  },
  getRecordedGestures: () => recorded,
};
