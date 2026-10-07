// Skip rules: skips a song as soon as it starts when it matches one of the
// rules in Settings. Spotify does not tell apps in advance what comes next,
// so a skipped song may be heard for a moment.
'use strict';

const LOOP_LIMIT = 8; // skips in a row...
const LOOP_SECONDS = 20; // ...within this long means every song matches
const PAUSE_AFTER_LOOP_SECONDS = 60;
const MAX_ARTIST_COMMANDS = 3;

let recentSkips = 0;
let loopTimer = null;
let pausedUntilTimer = null;
let skipped = 0;

function lower(text) {
    return String(text || '').trim().toLowerCase();
}

function artistNames(state) {
    return String(state.artists || '').split(', ').map(name => name.trim()).filter(name => name.length > 0);
}

function songLine(state) {
    return state.title + ' — ' + state.artists;
}

// The version part of a title: what is in brackets or after a dash, as in
// "Song (Live)", "Song [Remix]" or "Song - Sped Up". "Live Forever" has none.
function versionParts(title) {
    const parts = [];
    const brackets = /[([]([^)\]]*)[)\]]/g;
    let match;
    while ((match = brackets.exec(title)) !== null) {
        parts.push(match[1]);
    }

    const dash = title.search(/\s[-–—]\s/);
    if (dash >= 0) {
        parts.push(title.slice(dash + 3));
    }

    return parts.map(lower);
}

function hasWord(text, word) {
    const escaped = word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp('(^|[^\\p{L}\\p{N}])' + escaped + '($|[^\\p{L}\\p{N}])', 'u').test(text);
}

// Why the song should be skipped, or null to let it play.
function reasonToSkip(state) {
    if (!state || !state.title) {
        return null;
    }

    const blockedArtists = resonate.settings.get('artists').map(lower);
    const names = artistNames(state).map(lower);
    const artist = blockedArtists.find(blocked => names.includes(blocked) || lower(state.artists) === blocked);
    if (artist) {
        return 'artist';
    }

    const songs = resonate.settings.get('songs').map(lower);
    if (songs.includes(lower(songLine(state)))) {
        return 'song';
    }

    const parts = versionParts(state.title);
    const version = resonate.settings.get('versions').map(lower).find(word => parts.some(part => hasWord(part, word)));
    if (version) {
        return 'version: ' + version;
    }

    const longerThan = resonate.settings.get('longerThan');
    if (longerThan > 0 && state.duration > longerThan * 60) {
        return 'longer than ' + longerThan + ' min';
    }

    const shorterThan = resonate.settings.get('shorterThan');
    if (shorterThan > 0 && state.duration > 0 && state.duration < shorterThan) {
        return 'shorter than ' + shorterThan + ' s';
    }

    return null;
}

function check(state) {
    if (pausedUntilTimer !== null || !state || !state.isPlaying) {
        return;
    }

    const reason = reasonToSkip(state);
    if (reason === null) {
        return;
    }

    recentSkips += 1;
    if (loopTimer === null) {
        loopTimer = setTimeout(() => {
            recentSkips = 0;
            loopTimer = null;
        }, LOOP_SECONDS * 1000);
    }

    if (recentSkips > LOOP_LIMIT) {
        // Every song matches (a playlist of live versions, say): stop for a while instead of skipping forever.
        pausedUntilTimer = setTimeout(() => {
            pausedUntilTimer = null;
            resonate.ui.setStatus(null);
        }, PAUSE_AFTER_LOOP_SECONDS * 1000);
        resonate.ui.setStatus('Paused for a minute: every song matched');
        resonate.ui.notify('Skip rules matched every song in a row, so they are paused for a minute.');
        return;
    }

    skipped += 1;
    resonate.ui.setStatus('Skipped ' + skipped + (skipped === 1 ? ' song' : ' songs') + ', last: ' + state.title + ' (' + reason + ')');
    resonate.player.next();
}

function addToList(key, value) {
    const list = resonate.settings.get(key);
    if (!list.map(lower).includes(lower(value))) {
        list.push(value);
        resonate.settings.set(key, list);
    }
}

// "Always skip this song" and one "Always skip <artist>" per artist of the song playing.
function updateCommands(state) {
    const commands = [];
    if (state && state.title) {
        commands.push({
            id: 'song',
            title: 'Always skip this song',
            run: () => {
                const now = resonate.player.state;
                if (now && now.title) {
                    addToList('songs', songLine(now));
                    check(now);
                }
            },
        });

        artistNames(state).slice(0, MAX_ARTIST_COMMANDS).forEach((name, index) => {
            commands.push({
                id: 'artist-' + index,
                title: 'Always skip ' + name,
                run: () => {
                    addToList('artists', name);
                    check(resonate.player.state);
                },
            });
        });
    }

    resonate.ui.setCommands(commands);
}

resonate.player.onTrackChange(state => {
    updateCommands(state);
    check(state);
});

// A song that was paused when it started is checked once it plays (a new song is checked above).
resonate.player.onChange((state, before) => {
    if (state && state.isPlaying && before && !before.isPlaying && state.title === before.title && state.artists === before.artists) {
        check(state);
    }
});

updateCommands(resonate.player.state);
