const { View } = require('react-native');

function useSharedValue(init) {
  const shared = {
    value: init,
    get() {
      return shared.value;
    },
    set(next) {
      shared.value = typeof next === 'function' ? next(shared.value) : next;
    },
  };
  return shared;
}

function useAnimatedStyle(updater) {
  return typeof updater === 'function' ? updater() : updater;
}

function withSpring(toValue) {
  return toValue;
}

function runOnJS(fn) {
  return fn;
}

function cancelAnimation() {
  // This mock updates shared values synchronously, so no animation is pending.
}

const AnimatedView = View;

module.exports = {
  __esModule: true,
  default: { View: AnimatedView, createAnimatedComponent: (component) => component },
  View: AnimatedView,
  useSharedValue,
  useAnimatedStyle,
  withSpring,
  runOnJS,
  cancelAnimation,
};
