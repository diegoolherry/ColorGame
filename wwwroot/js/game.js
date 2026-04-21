let connection;
let myName = "";
let myRole = ""; // "Admin" or "Player"
let roomCode = "";
let currentPlayerName = "";
let timerInterval = null;
let turnStartTime = null;
let currentTimes = {};
let allPlayers = []; // Just names for the lobby

// DOM Elements
const sections = {
    initial: document.getElementById('section-initial'),
    lobby: document.getElementById('section-lobby'),
    game: document.getElementById('section-game'),
    gameover: document.getElementById('section-gameover')
};

// --- Utils ---
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

// --- SignalR Setup ---
function setupSignalR() {
    const overlay = document.getElementById('connection-overlay');
    overlay.classList.remove('d-none');
    document.getElementById('btn-create-room').disabled = true;
    document.getElementById('btn-join-room').disabled = true;

    connection = new signalR.HubConnectionBuilder()
        .withUrl("/gameHub")
        .withAutomaticReconnect()
        .build();

    // Reconnection events
    connection.onreconnecting(() => {
        overlay.classList.remove('d-none');
    });
    connection.onreconnected(() => {
        overlay.classList.add('d-none');
    });

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
        if (sections.lobby.classList.contains('d-none') === false) {
            updateLobby();
        }
    });

    connection.on("JoinError", (msg) => {
        const err = document.getElementById('join-error');
        err.textContent = msg;
        err.classList.remove('d-none');
    });

    connection.on("StartError", (msg) => {
        const err = document.getElementById('start-error');
        err.textContent = msg;
        err.classList.remove('d-none');
    });

    connection.on("GameStarted", (firstPlayer, playersList) => {
        currentPlayerName = firstPlayer;
        currentTimes = {};
        playersList.forEach(p => {
            currentTimes[p] = 0;
        });
        startGameTurn();
        showSection('game');
    });

    connection.on("NextTurn", (nextPlayer, submittedColor, prevPlayer) => {
        // Calculate and add elapsed time locally for UI smoothness, though server is truth
        if (turnStartTime) {
            const elapsed = (Date.now() - turnStartTime) / 1000;
            currentTimes[prevPlayer] = (currentTimes[prevPlayer] || 0) + elapsed;
        }

        currentPlayerName = nextPlayer;
        startGameTurn();
        updateTimesList();
    });

    connection.on("GameOver", (loserName, losingColor, totalSeconds, scores) => {
        stopTimer();
        document.getElementById('loser-name').textContent = loserName;
        document.getElementById('losing-color').textContent = losingColor;

        const timeFormatted = formatTime(totalSeconds);
        document.getElementById('total-time').textContent = timeFormatted;
        document.getElementById('total-time-display').textContent = timeFormatted;

        const tbody = document.getElementById('scores-table-body');
        tbody.innerHTML = '';
        scores.forEach((score, index) => {
            const tr = document.createElement('tr');
            const colorsText = (score.colors && score.colors.length > 0)
                ? score.colors.join(', ')
                : '—';
            const isLoser = score.name === loserName;
            tr.innerHTML = `
                <td class="ps-4">#${index + 1}</td>
                <td class="${isLoser ? 'text-danger fw-bold' : ''}">${score.name}</td>
                <td style="font-size: 0.85rem; color: rgba(255,255,255,0.75); max-width: 200px; word-break: break-word;">${colorsText}</td>
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

    connection.on("GameReset", (players) => {
        allPlayers = players;
        updateLobby();
        showSection('lobby');
    });

    connection.on("PlayerLeft", (leavingPlayerName, players) => {
        allPlayers = players;
        if (sections.lobby.classList.contains('d-none') === false) {
            updateLobby();
        }
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

// --- Action Functions ---
function updateLobby() {
    document.getElementById('lobby-code').textContent = roomCode;
    document.getElementById('lobby-player-count').textContent = allPlayers.length;
    
    // Check if we have enough players to start (admin + at least 2 players = 3, or if admin is part of list, just allPlayers > 2... wait real logic is >= 2 gameplayers)
    if (myRole === "Admin") {
        document.getElementById('btn-start-game').disabled = (allPlayers.length < 2);
    }

    const container = document.getElementById('lobby-players');
    container.innerHTML = '';
    allPlayers.forEach(p => {
        const badge = document.createElement('span');
        badge.className = 'badge bg-primary fs-5 py-2 px-3';
        // highlight admin? Simple heuristic: first in list is admin visually based on creation
        badge.textContent = p;
        container.appendChild(badge);
    });
}

function startGameTurn() {
    stopTimer();
    
    document.getElementById('current-player').textContent = currentPlayerName;
    document.getElementById('waiting-for').textContent = currentPlayerName;

    const myTurnControls = document.getElementById('my-turn-controls');
    const notMyTurnMessage = document.getElementById('not-my-turn-message');
    const colorInput = document.getElementById('color-input');

    if (isMyTurn()) {
        myTurnControls.classList.remove('d-none');
        notMyTurnMessage.classList.add('d-none');
        colorInput.value = '';
        colorInput.focus();
    } else {
        myTurnControls.classList.add('d-none');
        notMyTurnMessage.classList.remove('d-none');
    }

    turnStartTime = Date.now();
    timerInterval = setInterval(() => {
        const elapsed = (Date.now() - turnStartTime) / 1000;
        document.getElementById('timer-display').textContent = formatTime(elapsed);
        
        let header = document.getElementById('turn-header');
        if (isMyTurn()) {
            if (!header.classList.contains('text-bg-warning')) {
                header.className = 'mb-4 p-2 rounded text-bg-warning';
            }
        } else {
            header.className = 'mb-4';
        }

    }, 100);
}

function updateTimesList() {
    const list = document.getElementById('game-times-list');
    list.innerHTML = '';
    // Sort times
    const sorted = Object.entries(currentTimes).sort((a,b) => a[1] - b[1]);
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

    stopTimer();
    const elapsed = (Date.now() - turnStartTime) / 1000;
    
    // Disable input while communicating
    document.getElementById('my-turn-controls').classList.add('d-none');
    document.getElementById('not-my-turn-message').classList.remove('d-none');
    
    connection.invoke("SubmitColor", roomCode, color, elapsed).catch(err => {
        console.error(err.toString());
        // Re-enable if error
        document.getElementById('my-turn-controls').classList.remove('d-none');
        document.getElementById('not-my-turn-message').classList.add('d-none');
        startGameTurn(); // resume timer approx
    });
}

// --- Event Listeners ---
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
            setTimeout(() => btn.textContent = "📋 Copiar código", 2000);
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

    document.getElementById('btn-reset-game').addEventListener('click', () => {
        connection.invoke("ResetGame", roomCode).catch(err => console.error(err.toString()));
    });

    document.getElementById('btn-go-home').addEventListener('click', () => {
        location.reload();
    });
});
