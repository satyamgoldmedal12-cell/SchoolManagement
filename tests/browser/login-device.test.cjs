const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/login-device.js'), 'utf8');
const position = { coords: { latitude: 19.076, longitude: 72.8777, accuracy: 25 }, timestamp: 1793448000000 };

function page({ secure = true, geolocation = true, throws = false, initial = {} } = {}) {
    const fields = Object.fromEntries(['timezone', 'latitude', 'longitude', 'accuracy', 'captured', 'status']
        .map(name => [name, { value: initial[name] ?? (name === 'status' ? 'NotRequested' : '') }]));
    const status = { textContent: '' };
    const requests = [];
    const timers = new Map();
    const form = {
        handler: null, posts: [],
        addEventListener(event, handler) { assert.equal(event, 'submit'); this.handler = handler; },
        requestSubmit(submitter) {
            const event = { submitter, prevented: false, preventDefault() { this.prevented = true; } };
            const completed = this.handler?.(event);
            if (!event.prevented) this.posts.push({ ...Object.fromEntries(Object.entries(fields).map(([name, input]) => [name, input.value])), submitter });
            return { event, completed };
        }
    };
    const container = {
        closest(selector) { assert.equal(selector, 'form'); return form; },
        querySelector(selector) {
            const match = selector.match(/^\[data-device-field="(.+)"\]$/);
            if (match) return fields[match[1]];
            if (selector === '[data-device-status]') return status;
            throw new Error(`Unexpected selector: ${selector}`);
        }
    };
    const navigator = geolocation ? {
        geolocation: { getCurrentPosition(success, failure, options) {
            if (throws) throw new Error('Browser location unavailable');
            requests.push({ success, failure, options });
        } }
    } : {};
    vm.runInNewContext(source, {
        document: { querySelectorAll: () => [container] },
        window: { isSecureContext: secure }, navigator, Intl,
        setTimeout(callback, milliseconds) { assert.equal(milliseconds, 8000); const id = Symbol(); timers.set(id, callback); return id; },
        clearTimeout(id) { timers.delete(id); }
    });
    return { fields, status, requests, form, timers };
}

test('opening the form automatically requests location without a sharing button', () => {
    const form = page();
    assert.equal(form.requests.length, 1);
    assert.equal(form.fields.status.value, 'Pending');
    assert.ok(form.fields.timezone.value);
    assert.equal(form.requests[0].options.timeout, 8000);
});

test('permission allows coordinates, accuracy and timestamp to accompany login', () => {
    const pageState = page();
    pageState.requests[0].success(position);
    assert.equal(pageState.fields.latitude.value, '19.076');
    assert.equal(pageState.fields.longitude.value, '72.8777');
    assert.equal(pageState.fields.accuracy.value, '25');
    assert.equal(pageState.fields.captured.value, '1793448000000');
    assert.equal(pageState.fields.status.value, 'Granted');
    assert.equal(pageState.form.requestSubmit().event.prevented, false);
    assert.equal(pageState.form.posts[0].status, 'Granted');
});

test('denial clears old coordinates and still allows login', () => {
    const pageState = page({ initial: { latitude: '1', longitude: '2', accuracy: '3', captured: '4', status: 'Granted' } });
    pageState.requests[0].failure({ code: 1 });
    assert.equal(pageState.fields.status.value, 'Denied');
    for (const name of ['latitude', 'longitude', 'accuracy', 'captured']) assert.equal(pageState.fields[name].value, '');
    assert.match(pageState.status.textContent, /still log in/);
    assert.equal(pageState.form.requestSubmit().event.prevented, false);
});

test('unsupported or insecure browsers and synchronous errors allow login', () => {
    for (const options of [{ secure: false }, { geolocation: false }, { throws: true }]) {
        const pageState = page(options);
        assert.equal(pageState.requests.length, 0);
        assert.equal(pageState.fields.status.value, 'Unavailable');
        assert.match(pageState.status.textContent, /still log in/);
        assert.equal(pageState.form.requestSubmit().event.prevented, false);
    }
});

test('an early login waits for permission then submits once with the original button', async () => {
    const pageState = page();
    const button = { name: 'login' };
    const pending = pageState.form.requestSubmit(button);
    assert.equal(pending.event.prevented, true);
    await pageState.form.requestSubmit(button).completed;
    assert.equal(pageState.timers.size, 1);
    assert.equal(pageState.form.posts.length, 0);
    pageState.requests[0].success(position);
    await pending.completed;
    assert.equal(pageState.form.posts.length, 1);
    assert.equal(pageState.form.posts[0].submitter, button);
    assert.equal(pageState.form.posts[0].latitude, '19.076');
    assert.equal(pageState.timers.size, 0);
});

test('blocking permission releases a pending login', async () => {
    const pageState = page();
    const pending = pageState.form.requestSubmit();
    pageState.requests[0].failure({ code: 1 });
    await pending.completed;
    assert.equal(pageState.form.posts.length, 1);
    assert.equal(pageState.form.posts[0].status, 'Denied');
});

test('ignoring the popup cannot indefinitely block login or accept a late location', async () => {
    const pageState = page();
    const pending = pageState.form.requestSubmit();
    [...pageState.timers.values()][0]();
    await pending.completed;
    assert.equal(pageState.form.posts.length, 1);
    assert.equal(pageState.form.posts[0].status, 'Timeout');
    pageState.requests[0].success(position);
    assert.equal(pageState.fields.status.value, 'Timeout');
    assert.equal(pageState.fields.latitude.value, '');
});

test('geolocation timeout is recorded without coordinates', () => {
    const pageState = page();
    pageState.requests[0].failure({ code: 3 });
    assert.equal(pageState.fields.status.value, 'Timeout');
    assert.equal(pageState.fields.captured.value, '');
    assert.equal(pageState.form.requestSubmit().event.prevented, false);
});

test('invalid browser coordinates are discarded', () => {
    for (const coords of [
        { latitude: NaN, longitude: 72, accuracy: 10 },
        { latitude: 91, longitude: 72, accuracy: 10 },
        { latitude: 19, longitude: 181, accuracy: 10 },
        { latitude: 19, longitude: 72, accuracy: -1 }
    ]) {
        const pageState = page();
        pageState.requests[0].success({ coords, timestamp: position.timestamp });
        assert.equal(pageState.fields.status.value, 'Unavailable');
        assert.equal(pageState.fields.latitude.value, '');
    }
});
