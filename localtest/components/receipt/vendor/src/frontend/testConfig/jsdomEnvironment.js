const FixedJSDOMEnvironment = require('jest-fixed-jsdom');

/**
 * jsdom 26 (bundled with jest 30) marks `window.location` as
 * [LegacyUnforgeable], so it can no longer be replaced or spied on with
 * `jest.spyOn(window, 'location', 'get')`. Expose the underlying jsdom instance
 * so tests can navigate via `jsdom.reconfigure({ url })` instead.
 */
class JSDOMEnvironment extends FixedJSDOMEnvironment {
  constructor(...args) {
    super(...args);
    this.global.jsdom = this.dom;
  }
}

module.exports = JSDOMEnvironment;
