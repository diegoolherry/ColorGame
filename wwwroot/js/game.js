let connection;
let myName = "";
let myRole = ""; // "Admin" or "Player"
let roomCode = "";
let currentPlayerName = "";
let timerInterval = null;
let turnStartTime = null;
let currentTimes = {};
let allPlayers = [];
let isFirstTurn = false;       // True when it's turn 0 of a partida
let currentBannedColors = [];  // Colors banned from previous partida

// DOM sections
const sections = {
    initial:     document.getElementById('section-initial'),
    lobby:       document.getElementById('section-lobby'),
    game:        document.getElementById('section-game'),
    gameover:    document.getElementById('section-gameover'),
    leaderboard: document.getElementById('section-leaderboard')
};

// ─────────────────────────────────────────────────────────────────────────────
// UTILS
// ─────────────────────────────────────────────────────────────────────────────

function showSection(id) {
    Object.values(sections).forEach(s => s.classList.add('d-none'));
    sections[id].classList.remove('d-none');
}

function stopTimer() {
    if (timerInterval) {
        clearInterval(timerInterval);
        timerInterval = null;
    }
}

function isMyTurn() {
    return myName === currentPlayerName;
}

function formatTime(seconds) {
    let m = Math.floor(seconds / 60);
    let s = Math.floor(seconds % 60);
    let d = Math.floor((seconds * 10) % 10);
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}.${d}`;
}

function renderBannedColors(bannedColors) {
    currentBannedColors = bannedColors || [];
    const panel = document.getElementById('banned-colors-panel');
    const list  = document.getElementById('banned-colors-list');
    list.innerHTML = '';

    if (currentBannedColors.length > 0) {
        panel.classList.remove('d-none');
        currentBannedColors.forEach(c => {
            const span = document.createElement('span');
            span.className = 'banned-color-badge';
            span.textContent = c;
            list.appendChild(span);
        });
    } else {
        panel.classList.add('d-none');
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SIGNALR SETUP
// ─────────────────────────────────────────────────────────────────────────────

function setupSignalR() {
    const overlay = document.getElementById('connection-overlay');
    overlay.classList.remove('d-none');
    document.getElementById('btn-create-room').disabled = true;
    document.getElementById('btn-join-room').disabled = true;

    connection = new signalR.HubConnectionBuilder()
        .withUrl("/gameHub")
        .withAutomaticReconnect()
        .build();

    connection.onreconnecting(() => overlay.classList.remove('d-none'));
    connection.onreconnected(()  => overlay.classList.add('d-none'));

    // ── Room events ──────────────────────────────────────────────────────────

    connection.on("RoomCreated", (code) => {
        roomCode = code;
        myRole = "Admin";
        allPlayers = [myName];
        updateLobby();
        showSection('lobby');
        document.getElementById('lobby-admin-controls').classList.remove('d-none');
        document.getElementById('lobby-player-message').classList.add('d-none');
    });

    connection.on("JoinedRoom", (code, players) => {
        roomCode = code;
        myRole = "Player";
        allPlayers = players;
        updateLobby();
        showSection('lobby');
        document.getElementById('lobby-admin-controls').classList.add('d-none');
        document.getElementById('lobby-player-message').classList.remove('d-none');
    });

    connection.on("PlayerJoined", (newPlayerName, players) => {
        allPlayers = players;
        if (!sections.lobby.classList.contains('d-none')) updateLobby();
    });

    connection.on("JoinError",  (msg) => { showError('join-error',  msg); });
    connection.on("StartError", (msg) => { showError('start-error', msg); });

    // ── Game events ──────────────────────────────────────────────────────────

    connection.on("GameStarted", (firstPlayer, playersList, bannedColors, partidaNumber) => {
        currentPlayerName = firstPlayer;
        currentTimes = {};
        playersList.forEach(p => { currentTimes[p] = 0; });

        // Banned colors from previous partida
        renderBannedColors(bannedColors);

        // Update partida badge
        document.getElementById('partida-badge').textContent = `Partida #${partidaNumber}`;
        document.getElementById('partida-name-display').textContent = '';

        // First turn: first player must name the partida
        isFirstTurn = (myName === firstPlayer);

        startGameTurn();
        showSection('game');
    });

    connection.on("NextTurn", (nextPlayer, submittedColor, prevPlayer) => {
        if (turnStartTime) {
            const elapsed = (Date.now() - turnStartTime) / 1000;
            currentTimes[prevPlayer] = (currentTimes[prevPlayer] || 0) + elapsed;
        }
        currentPlayerName = nextPlayer;
        isFirstTurn = false;   // Only turn 0 is "first"
        startGameTurn();
        updateTimesList();
    });

    connection.on("GameOver", (loserName, losingColor, totalSeconds, scores, partidaName) => {
        stopTimer();
        document.getElementById('loser-name').textContent = loserName;
        document.getElementById('losing-color').textContent = losingColor;
        document.getElementById('gameover-partida-name').textContent =
            partidaName ? `Partida: "${partidaName}"` : '';

        const timeFormatted = formatTime(totalSeconds);
        document.getElementById('total-time').textContent = timeFormatted;
        document.getElementById('total-time-display').textContent = timeFormatted;

        const tbody = document.getElementById('scores-table-body');
        tbody.innerHTML = '';
        scores.forEach((score, index) => {
            const tr = document.createElement('tr');
            const colorsText = (score.colors && score.colors.length > 0)
                ? score.colors.join(', ') : '—';
            tr.innerHTML = `
                <td class="ps-4">#${index + 1}</td>
                <td class="${score.isLoser ? 'text-danger fw-bold' : ''}">${score.name}</td>
                <td style="font-size:0.85rem;color:rgba(255,255,255,0.75);max-width:200px;word-break:break-word;">${colorsText}</td>
                <td class="pe-4 text-end">${formatTime(score.accumulatedSeconds)}</td>
            `;
            tbody.appendChild(tr);
        });

        if (myRole === "Admin") {
            document.getElementById('gameover-admin-controls').classList.remove('d-none');
            document.getElementById('gameover-player-message').classList.add('d-none');
        } else {
            document.getElementById('gameover-admin-controls').classList.add('d-none');
            document.getElementById('gameover-player-message').classList.remove('d-none');
        }
        showSection('gameover');
    });

    // Admin: next partida within same round
    connection.on("NextPartidaReady", (players, bannedColors) => {
        allPlayers = players;
        updateLobby();
        // Go straight back to lobby so admin can re-start
        showSection('lobby');
        // Show banned colors info in lobby for reference
        if (bannedColors.length > 0) {
            const info = document.getElementById('start-error');
            info.className = 'alert mt-3 fw-bold border-0';
            info.style.background = 'rgba(255,180,60,0.2)';
            info.style.color = '#FFB43C';
            info.textContent = `🚫 Colores prohibidos esta ronda: ${bannedColors.join(', ')}`;
            info.classList.remove('d-none');
        }
    });

    // Admin: end round → leaderboard
    connection.on("RoundEnded", (leaderboard) => {
        const tbody = document.getElementById('leaderboard-table-body');
        tbody.innerHTML = '';
        leaderboard.forEach((entry, index) => {
            const medalClass = index === 0 ? 'medal-1' : index === 1 ? 'medal-2' : index === 2 ? 'medal-3' : '';
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td class="ps-4 ${medalClass} fw-bold">${index === 0 ? '🥇' : index === 1 ? '🥈' : index === 2 ? '🥉' : '#' + entry.position}</td>
                <td class="fw-bold">${entry.name || '—'}</td>
                <td class="${entry.loserName ? 'text-danger' : ''}">${entry.loserName || '—'}</td>
                <td style="color:rgba(255,255,255,0.65);">${entry.losingColor || '—'}</td>
                <td class="pe-4 text-end fw-bold" style="color:#00C9FF;">${formatTime(entry.totalSeconds)}</td>
            `;
            tbody.appendChild(tr);
        });

        if (myRole === "Admin") {
            document.getElementById('leaderboard-admin-controls').classList.remove('d-none');
        } else {
            document.getElementById('leaderboard-admin-controls').classList.add('d-none');
        }
        showSection('leaderboard');
    });

    connection.on("GameReset", (players) => {
        allPlayers = players;
        // Clear banned color info alert
        const info = document.getElementById('start-error');
        info.classList.add('d-none');
        info.style.background = '';
        info.style.color = '';
        updateLobby();
        showSection('lobby');
    });

    connection.on("PlayerLeft", (leavingPlayerName, players) => {
        allPlayers = players;
        if (!sections.lobby.classList.contains('d-none')) updateLobby();
    });

    connection.on("AdminLeft", () => {
        alert("El administrador ha abandonado la sala. Volviendo al inicio.");
        location.reload();
    });

    connection.start().then(() => {
        overlay.classList.add('d-none');
        document.getElementById('btn-create-room').disabled = false;
        document.getElementById('btn-join-room').disabled = false;
    }).catch(err => console.error(err.toString()));
}

// ─────────────────────────────────────────────────────────────────────────────
// ACTION FUNCTIONS
// ─────────────────────────────────────────────────────────────────────────────

function showError(id, msg) {
    const el = document.getElementById(id);
    el.textContent = msg;
    el.className = 'alert alert-danger mt-3 fw-bold bg-danger text-white border-0';
    el.classList.remove('d-none');
}

function updateLobby() {
    document.getElementById('lobby-code').textContent = roomCode;
    document.getElementById('lobby-player-count').textContent = allPlayers.length;

    if (myRole === "Admin") {
        document.getElementById('btn-start-game').disabled = (allPlayers.length < 2);
    }

    const container = document.getElementById('lobby-players');
    container.innerHTML = '';
    allPlayers.forEach(p => {
        const badge = document.createElement('span');
        badge.className = 'badge bg-primary fs-5 py-2 px-3';
        badge.textContent = p;
        container.appendChild(badge);
    });
}

function startGameTurn() {
    stopTimer();

    document.getElementById('current-player').textContent = currentPlayerName;
    document.getElementById('waiting-for').textContent = currentPlayerName;

    const myTurnControls     = document.getElementById('my-turn-controls');
    const notMyTurnMessage   = document.getElementById('not-my-turn-message');
    const partidaNameSection = document.getElementById('partida-name-section');
    const colorInput         = document.getElementById('color-input');

    if (isMyTurn()) {
        myTurnControls.classList.remove('d-none');
        notMyTurnMessage.classList.add('d-none');
        colorInput.value = '';

        // Show partida naming input only on first turn
        if (isFirstTurn) {
            partidaNameSection.classList.remove('d-none');
            document.getElementById('partida-name-input').value = '';
            document.getElementById('partida-name-input').focus();
        } else {
            partidaNameSection.classList.add('d-none');
            colorInput.focus();
        }
    } else {
        myTurnControls.classList.add('d-none');
        notMyTurnMessage.classList.remove('d-none');
        partidaNameSection.classList.add('d-none');
    }

    turnStartTime = Date.now();
    timerInterval = setInterval(() => {
        const elapsed = (Date.now() - turnStartTime) / 1000;
        document.getElementById('timer-display').textContent = formatTime(elapsed);

        const header = document.getElementById('turn-header');
        if (isMyTurn()) {
            if (!header.classList.contains('text-bg-warning'))
                header.className = 'mb-4 p-2 rounded text-bg-warning';
        } else {
            header.className = 'mb-4';
        }
    }, 100);
}

function updateTimesList() {
    const list = document.getElementById('game-times-list');
    list.innerHTML = '';
    const sorted = Object.entries(currentTimes).sort((a, b) => a[1] - b[1]);
    sorted.forEach(([name, time]) => {
        const li = document.createElement('li');
        li.className = 'list-group-item d-flex justify-content-between align-items-center';
        li.innerHTML = `<span>${name}</span> <strong>${formatTime(time)}</strong>`;
        list.appendChild(li);
    });
}

function submitColor() {
    if (!isMyTurn()) return;
    const color = document.getElementById('color-input').value.trim();
    if (!color) return;

    // Get partida name if it's the first turn
    let partidaName = null;
    if (isFirstTurn) {
        partidaName = document.getElementById('partida-name-input').value.trim() || null;
    }

    stopTimer();
    const elapsed = (Date.now() - turnStartTime) / 1000;

    document.getElementById('my-turn-controls').classList.add('d-none');
    document.getElementById('not-my-turn-message').classList.remove('d-none');
    document.getElementById('partida-name-section').classList.add('d-none');

    // Update partida name display if provided
    if (isFirstTurn && partidaName) {
        document.getElementById('partida-name-display').textContent = `"${partidaName}"`;
    }

    connection.invoke("SubmitColor", roomCode, color, elapsed, partidaName).catch(err => {
        console.error(err.toString());
        document.getElementById('my-turn-controls').classList.remove('d-none');
        document.getElementById('not-my-turn-message').classList.add('d-none');
        startGameTurn();
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// EVENT LISTENERS
// ─────────────────────────────────────────────────────────────────────────────

document.addEventListener("DOMContentLoaded", () => {
    setupSignalR();

    document.getElementById('btn-create-room').addEventListener('click', () => {
        const name = document.getElementById('admin-name').value.trim();
        if (!name) return;
        myName = name;
        document.getElementById('create-error').classList.add('d-none');
        connection.invoke("CreateRoom", myName).catch(err => console.error(err.toString()));
    });

    document.getElementById('btn-join-room').addEventListener('click', () => {
        const name = document.getElementById('player-name').value.trim();
        const code = document.getElementById('join-code').value.trim();
        if (!name || !code) return;
        myName = name;
        document.getElementById('join-error').classList.add('d-none');
        connection.invoke("JoinRoom", code, myName).catch(err => console.error(err.toString()));
    });

    document.getElementById('btn-copy-code').addEventListener('click', () => {
        navigator.clipboard.writeText(roomCode).then(() => {
            const btn = document.getElementById('btn-copy-code');
            btn.textContent = "✅ Copiado!";
            setTimeout(() => btn.textContent = "📋 Copiar al portapapeles", 2000);
        });
    });

    document.getElementById('btn-start-game').addEventListener('click', () => {
        document.getElementById('start-error').classList.add('d-none');
        connection.invoke("StartGame", roomCode).catch(err => console.error(err.toString()));
    });

    document.getElementById('btn-submit-color').addEventListener('click', submitColor);

    document.getElementById('color-input').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') submitColor();
    });

    // Tab to skip from partida name to color input
    document.getElementById('partida-name-input').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') document.getElementById('color-input').focus();
    });

    // Admin gameover controls
    document.getElementById('btn-next-partida').addEventListener('click', () => {
        connection.invoke("NextPartida", roomCode).catch(err => console.error(err.toString()));
    });

    document.getElementById('btn-end-round').addEventListener('click', () => {
        connection.invoke("EndRound", roomCode).catch(err => console.error(err.toString()));
    });

    // Leaderboard controls
    document.getElementById('btn-new-round').addEventListener('click', () => {
        connection.invoke("ResetGame", roomCode).catch(err => console.error(err.toString()));
    });

    document.getElementById('btn-go-home').addEventListener('click', () => location.reload());
    document.getElementById('btn-leaderboard-home').addEventListener('click', () => location.reload());
});
