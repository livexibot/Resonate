// Runs before every plugin, in that plugin's own JavaScript engine. It
// builds the `resonate` object, timers and the console from the few
// functions the helper passes in as `host`, and returns the function the
// helper calls with events. Plugins never reach `host` itself, and every
// call they make is checked again by the helper and by Resonate.
(function (host) {
    'use strict';

    const changeHandlers = [];
    const trackHandlers = [];
    const settingsHandlers = [];
    const commands = new Map();
    const timers = new Map();
    let nextTimer = 1;
    let current = null;
    let receivedAt = 0;
    let settings = {};
    let storage = {};

    function describe(error) {
        if (error !== null && typeof error === 'object' && 'message' in error) {
            return (error.name ? error.name + ': ' : '') + String(error.message);
        }

        return String(error);
    }

    function report(error) {
        host.error(describe(error));
    }

    function text(value) {
        if (typeof value === 'string') {
            return value;
        }

        try {
            return JSON.stringify(value);
        } catch (e) {
            return String(value);
        }
    }

    function copy(value) {
        return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
    }

    function each(handlers, args) {
        for (const handler of handlers.slice()) {
            try {
                handler.apply(undefined, args);
            } catch (e) {
                report(e);
            }
        }
    }

    function needFunction(fn, what) {
        if (typeof fn !== 'function') {
            throw new TypeError(what + ' needs a function.');
        }
    }

    function subscribe(list, fn, what) {
        needFunction(fn, what);
        list.push(fn);
        return function unsubscribe() {
            const index = list.indexOf(fn);
            if (index >= 0) {
                list.splice(index, 1);
            }
        };
    }

    // What is playing now: the position moves on from when Resonate last reported it.
    function snapshot() {
        if (current === null) {
            return null;
        }

        const state = Object.assign({}, current);
        if (state.isPlaying) {
            const moved = state.position + (host.now() - receivedAt) / 1000;
            state.position = state.duration > 0 ? Math.min(state.duration, moved) : moved;
        }

        return Object.freeze(state);
    }

    function sameTrack(a, b) {
        if (a === null || b === null) {
            return a === b;
        }

        if (a.uri && b.uri) {
            return a.uri === b.uri;
        }

        return a.title === b.title && a.artists === b.artists;
    }

    function sendCommands() {
        host.commands(Array.from(commands.values(), c => ({ id: c.id, title: c.title })));
    }

    function startTimer(fn, ms, repeat, what) {
        needFunction(fn, what);
        const id = nextTimer++;
        timers.set(id, { fn, repeat });
        host.setTimer(id, Number(ms) || 0, repeat);
        return id;
    }

    function stopTimer(id) {
        if (timers.delete(id)) {
            host.clearTimer(id);
        }
    }

    const player = Object.freeze({
        get state() {
            return snapshot();
        },
        onChange: fn => subscribe(changeHandlers, fn, 'player.onChange'),
        onTrackChange: fn => subscribe(trackHandlers, fn, 'player.onTrackChange'),
        play() {
            host.player('play', 0);
        },
        pause() {
            host.player('pause', 0);
        },
        next() {
            host.player('next', 0);
        },
        previous() {
            host.player('previous', 0);
        },
        seek(seconds) {
            host.player('seek', Number(seconds));
        },
        setVolume(volume) {
            host.player('volume', Number(volume));
        },
    });

    const settingsApi = Object.freeze({
        get(key) {
            return copy(settings[key]);
        },
        get all() {
            return copy(settings);
        },
        // Resonate checks the value against the setting's type and sends the result back (onChange).
        set(key, value) {
            key = String(key);
            if (!Object.prototype.hasOwnProperty.call(settings, key)) {
                throw new RangeError('This plugin has no setting "' + key + '".');
            }

            settings[key] = copy(value);
            host.setting(key, JSON.stringify(value === undefined ? null : value));
        },
        onChange: fn => subscribe(settingsHandlers, fn, 'settings.onChange'),
    });

    const storageApi = Object.freeze({
        get(key) {
            return copy(storage[String(key)]);
        },
        set(key, value) {
            storage[String(key)] = copy(value);
            host.storage(JSON.stringify(storage));
        },
        remove(key) {
            key = String(key);
            if (Object.prototype.hasOwnProperty.call(storage, key)) {
                delete storage[key];
                host.storage(JSON.stringify(storage));
            }
        },
        keys() {
            return Object.keys(storage);
        },
    });

    const ui = Object.freeze({
        addCommand(id, title, run) {
            needFunction(run, 'ui.addCommand');
            id = String(id);
            commands.set(id, { id, title: String(title), run });
            sendCommands();
        },
        removeCommand(id) {
            if (commands.delete(String(id))) {
                sendCommands();
            }
        },
        // Replaces every command at once: a list of { id, title, run }.
        setCommands(list) {
            if (!Array.isArray(list)) {
                throw new TypeError('ui.setCommands needs a list.');
            }

            const next = new Map();
            for (const command of list) {
                needFunction(command.run, 'ui.setCommands');
                const id = String(command.id);
                next.set(id, { id, title: String(command.title), run: command.run });
            }

            commands.clear();
            next.forEach((command, id) => commands.set(id, command));
            sendCommands();
        },
        setStatus(value) {
            host.status(value === null || value === undefined ? '' : String(value));
        },
        notify(value) {
            host.notify(String(value));
        },
    });

    const log = level => (...parts) => host.log(level, parts.map(text).join(' '));

    globalThis.resonate = Object.freeze({ player, settings: settingsApi, storage: storageApi, ui });
    globalThis.setTimeout = (fn, ms) => startTimer(fn, ms, false, 'setTimeout');
    globalThis.setInterval = (fn, ms) => startTimer(fn, ms, true, 'setInterval');
    globalThis.clearTimeout = stopTimer;
    globalThis.clearInterval = stopTimer;
    globalThis.console = Object.freeze({ log: log('info'), info: log('info'), warn: log('info'), error: log('error') });

    return function dispatch(kind, payload) {
        switch (kind) {
            case 'init': {
                const init = JSON.parse(payload);
                settings = init.settings || {};
                storage = init.storage || {};
                break;
            }

            case 'state': {
                const before = snapshot();
                current = JSON.parse(payload);
                receivedAt = host.now();
                const after = snapshot();
                each(changeHandlers, [after, before]);
                if (!sameTrack(before, after)) {
                    each(trackHandlers, [after, before]);
                }

                break;
            }

            case 'settings': {
                if (JSON.stringify(settings) === payload) {
                    break;
                }

                const before = settings;
                settings = JSON.parse(payload);
                each(settingsHandlers, [copy(settings), copy(before)]);
                break;
            }

            case 'invoke': {
                const command = commands.get(payload);
                if (command) {
                    try {
                        command.run();
                    } catch (e) {
                        report(e);
                    }
                }

                break;
            }

            case 'timer': {
                const id = Number(payload);
                const timer = timers.get(id);
                if (timer) {
                    if (!timer.repeat) {
                        timers.delete(id);
                    }

                    try {
                        timer.fn();
                    } catch (e) {
                        report(e);
                    }
                }

                break;
            }
        }
    };
})
