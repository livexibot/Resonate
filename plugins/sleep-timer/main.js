// Sleep timer: pauses the music after a set time, or when the song that is
// playing ends, with an optional fade over the last minute. The fade lowers
// Spotify's volume and puts it back after pausing, so the next listen
// starts at the usual volume.
'use strict';

const FADE_SECONDS = 60;
const FADE_STEP_SECONDS = 2;
const LOWEST_FADE = 0.05;
const PRESETS = [15, 30, 60];

let mode = null; // null, 'time' or 'song'
let secondsLeft = 0; // in 'time' mode
let ticker = null;
let songTimer = null;
let fadeFrom = null; // the volume before the fade started
let lastFadeStep = Infinity;

function minutesText(minutes) {
    if (minutes % 60 === 0) {
        return minutes === 60 ? '1 hour' : minutes / 60 + ' hours';
    }

    return minutes + ' minutes';
}

function clockText(seconds) {
    seconds = Math.max(0, Math.ceil(seconds));
    if (seconds >= 60) {
        return Math.ceil(seconds / 60) + ' min';
    }

    return '0:' + String(seconds).padStart(2, '0');
}

function songSecondsLeft() {
    const state = resonate.player.state;
    return state && state.duration > 0 ? state.duration - state.position : null;
}

function showStatus() {
    if (mode === 'time') {
        resonate.ui.setStatus('Pauses in ' + clockText(secondsLeft));
    } else if (mode === 'song') {
        resonate.ui.setStatus('Pauses at the end of this song');
    } else {
        resonate.ui.setStatus(null);
    }
}

function updateCommands() {
    if (mode === null) {
        const custom = resonate.settings.get('customMinutes');
        const choices = PRESETS.includes(custom) ? PRESETS : PRESETS.concat([custom]).sort((a, b) => a - b);
        resonate.ui.setCommands(
            choices
                .map(minutes => ({ id: 'in-' + minutes, title: 'Pause in ' + minutesText(minutes), run: () => startTime(minutes) }))
                .concat([{ id: 'end-of-song', title: 'Pause at the end of this song', run: startSong }]));
        return;
    }

    const commands = [];
    if (mode === 'time') {
        commands.push({
            id: 'add-15',
            title: 'Add 15 minutes',
            run: () => {
                secondsLeft += 15 * 60;
                restoreVolume();
                showStatus();
            },
        });
    }

    commands.push({ id: 'cancel', title: 'Cancel the sleep timer', run: () => stop(true) });
    resonate.ui.setCommands(commands);
}

// Lowers the volume in steps as the end comes closer.
function fade(secondsToGo) {
    if (!resonate.settings.get('fade') || secondsToGo > FADE_SECONDS) {
        return;
    }

    const state = resonate.player.state;
    if (fadeFrom === null) {
        if (!state || !state.isPlaying) {
            return;
        }

        fadeFrom = state.volume;
        lastFadeStep = Infinity;
    }

    if (lastFadeStep - secondsToGo < FADE_STEP_SECONDS) {
        return;
    }

    lastFadeStep = secondsToGo;
    const share = Math.max(LOWEST_FADE, secondsToGo / FADE_SECONDS);
    resonate.player.setVolume(fadeFrom * share);
}

function restoreVolume() {
    if (fadeFrom !== null) {
        resonate.player.setVolume(fadeFrom);
        fadeFrom = null;
    }
}

function finish() {
    const restore = fadeFrom;
    fadeFrom = null;
    stop(false);
    resonate.player.pause();
    if (restore !== null) {
        // After the pause has landed, so the music does not get louder before it stops.
        setTimeout(() => resonate.player.setVolume(restore), 1500);
    }
}

function tick() {
    if (mode === 'time') {
        secondsLeft -= 1;
        if (secondsLeft <= 0) {
            finish();
            return;
        }

        fade(secondsLeft);
        showStatus();
    } else if (mode === 'song') {
        const left = songSecondsLeft();
        if (left !== null) {
            fade(left);
        }
    }
}

// In 'song' mode a one-off timer is aimed just before the song's end, and
// aimed again whenever Resonate reports a new position (a seek, a pause).
function aimAtSongEnd() {
    if (songTimer !== null) {
        clearTimeout(songTimer);
        songTimer = null;
    }

    const state = resonate.player.state;
    const left = songSecondsLeft();
    if (mode !== 'song' || left === null || !state.isPlaying) {
        return;
    }

    songTimer = setTimeout(finish, Math.max(0, left - 0.4) * 1000);
}

function clearTimers() {
    if (ticker !== null) {
        clearInterval(ticker);
        ticker = null;
    }

    if (songTimer !== null) {
        clearTimeout(songTimer);
        songTimer = null;
    }
}

function startTime(minutes) {
    clearTimers();
    restoreVolume();
    mode = 'time';
    secondsLeft = Math.round(minutes * 60);
    ticker = setInterval(tick, 1000);
    showStatus();
    updateCommands();
}

function startSong() {
    clearTimers();
    restoreVolume();
    mode = 'song';
    ticker = setInterval(tick, 1000);
    aimAtSongEnd();
    showStatus();
    updateCommands();
}

function stop(restore) {
    clearTimers();
    if (restore) {
        restoreVolume();
    }

    mode = null;
    showStatus();
    updateCommands();
}

resonate.player.onChange(() => aimAtSongEnd());
resonate.settings.onChange(() => {
    if (mode === null) {
        updateCommands();
    }
});

updateCommands();
