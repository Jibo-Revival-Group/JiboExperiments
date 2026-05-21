const API_BASE = '/api/panel';
let refreshInterval = 5000; // Default 5 seconds
let refreshTimer = null;
let isConnected = false;
let autoScrollEnabled = true;
let currentTab = 'dashboard';

// Initialize the panel
async function init() {
    try {
        // Fetch configuration first to get refresh interval
        const status = await fetchStatus();
        if (status && status.configuration) {
            refreshInterval = (status.configuration.refreshIntervalSeconds || 5) * 1000;
        }
        
        // Initial data load
        await refreshAll();
        
        // Set up auto-refresh
        startAutoRefresh();
        
        // Update connection status
        setConnectionStatus(true);
        
        // Start terminal if on terminal tab
        if (currentTab === 'terminal') {
            startTerminal();
        }
    } catch (error) {
        console.error('Failed to initialize panel:', error);
        setConnectionStatus(false);
        // Retry after 5 seconds
        setTimeout(init, 5000);
    }
}

// Tab switching
function switchTab(tabName) {
    currentTab = tabName;
    
    // Update navigation items
    document.querySelectorAll('.nav-item').forEach(item => {
        item.classList.remove('active');
        if (item.dataset.tab === tabName) {
            item.classList.add('active');
        }
    });
    
    // Update tab content
    document.querySelectorAll('.main-content').forEach(content => {
        content.classList.remove('active');
    });
    
    const targetTab = document.getElementById(`tab-${tabName}`);
    if (targetTab) {
        targetTab.classList.add('active');
    }
    
    // Start terminal if switching to terminal tab
    if (tabName === 'terminal') {
        startTerminal();
    } else {
        stopTerminal();
    }
}

// Fetch server status
async function fetchStatus() {
    try {
        const response = await fetch(`${API_BASE}/status`);
        if (!response.ok) throw new Error('Failed to fetch status');
        return await response.json();
    } catch (error) {
        console.error('Error fetching status:', error);
        return null;
    }
}

// Fetch sessions
async function fetchSessions() {
    try {
        const response = await fetch(`${API_BASE}/sessions`);
        if (!response.ok) throw new Error('Failed to fetch sessions');
        return await response.json();
    } catch (error) {
        console.error('Error fetching sessions:', error);
        return null;
    }
}

// Fetch robots
async function fetchRobots() {
    try {
        const response = await fetch(`${API_BASE}/robots`);
        if (!response.ok) throw new Error('Failed to fetch robots');
        return await response.json();
    } catch (error) {
        console.error('Error fetching robots:', error);
        return null;
    }
}

// Fetch health
async function fetchHealth() {
    try {
        const response = await fetch(`${API_BASE}/health`);
        if (!response.ok) throw new Error('Failed to fetch health');
        return await response.json();
    } catch (error) {
        console.error('Error fetching health:', error);
        return null;
    }
}

// Refresh all data
async function refreshAll() {
    const [status, sessions, robots, health] = await Promise.all([
        fetchStatus(),
        fetchSessions(),
        fetchRobots(),
        fetchHealth()
    ]);

    if (status) updateStatus(status);
    if (sessions) updateSessions(sessions);
    if (robots) updateRobots(robots);
    if (health) updateHealth(health);
    
    updateLastRefresh();
}

// Update server status UI
function updateStatus(data) {
    document.getElementById('serverVersion').textContent = data.version || '-';
    document.getElementById('serverUptime').textContent = data.uptime || '-';
    document.getElementById('serverStartTime').textContent = formatDateTime(data.startTime) || '-';
    document.getElementById('lastSaved').textContent = formatDateTime(data.persistence?.lastSaved) || '-';
    
    if (data.configuration) {
        document.getElementById('webPanelEnabled').textContent = 
            data.configuration.webPanelEnabled ? 'Yes' : 'No';
        document.getElementById('refreshInterval').textContent = 
            `${data.configuration.refreshIntervalSeconds}s`;
        document.getElementById('remoteAccess').textContent = 
            data.configuration.allowRemoteAccess ? 'Yes' : 'No';
    }
}

// Update sessions UI
function updateSessions(data) {
    const count = data.count || 0;
    document.getElementById('sessionCount').textContent = count;
    
    const sessionsList = document.getElementById('sessionsList');
    if (count === 0 || !data.sessions || data.sessions.length === 0) {
        sessionsList.innerHTML = '<p class="empty-state">No active sessions</p>';
    } else {
        sessionsList.innerHTML = data.sessions.map(session => `
            <div class="session-item">
                <div class="session-info">
                    <span class="session-kind">${session.kind || 'Unknown'}</span>
                    <span class="session-token">${session.token || 'No token'}</span>
                </div>
                <div class="session-time">
                    Last seen: ${formatDateTime(session.lastSeenUtc)}
                </div>
            </div>
        `).join('');
    }
}

// Update robots UI
function updateRobots(data) {
    if (data.robots && data.robots.length > 0) {
        const robot = data.robots[0];
        document.getElementById('robotName').textContent = robot.friendlyName || 'Unknown Robot';
        document.getElementById('robotId').textContent = robot.robotId || '-';
        document.getElementById('deviceId').textContent = robot.deviceId || '-';
        document.getElementById('firmwareVersion').textContent = robot.firmwareVersion || '-';
        document.getElementById('appVersion').textContent = robot.applicationVersion || '-';
        document.getElementById('platform').textContent = robot.profile?.platform || '-';
    }
}

// Update health UI
function updateHealth(data) {
    const healthStatus = document.getElementById('healthStatus');
    healthStatus.textContent = data.status || '-';
    healthStatus.className = 'health-value';
    
    if (data.status === 'healthy') {
        healthStatus.classList.add('success');
    } else if (data.status === 'warning') {
        healthStatus.classList.add('warning');
    } else {
        healthStatus.classList.add('error');
    }
    
    if (data.checks) {
        const persistenceStatus = document.getElementById('persistenceStatus');
        persistenceStatus.textContent = data.checks.persistence?.status || '-';
        persistenceStatus.className = 'check-value';
        if (data.checks.persistence?.status === 'ok') {
            persistenceStatus.classList.add('success');
        } else if (data.checks.persistence?.status === 'warning') {
            persistenceStatus.classList.add('warning');
        } else {
            persistenceStatus.classList.add('error');
        }
        
        const stateStoreStatus = document.getElementById('stateStoreStatus');
        stateStoreStatus.textContent = data.checks.stateStore?.status || '-';
        stateStoreStatus.className = 'check-value';
        if (data.checks.stateStore?.status === 'ok') {
            stateStoreStatus.classList.add('success');
        } else {
            stateStoreStatus.classList.add('error');
        }
    }
}

// Update connection status indicator
function setConnectionStatus(connected) {
    isConnected = connected;
    const dot = document.getElementById('connectionStatus');
    const text = document.getElementById('connectionText');
    
    dot.className = 'status-dot ' + (connected ? 'connected' : 'disconnected');
    text.textContent = connected ? 'Connected' : 'Disconnected';
}

// Update last refresh time
function updateLastRefresh() {
    document.getElementById('lastUpdate').textContent = formatDateTime(new Date().toISOString());
    updateNextRefresh();
}

// Update next refresh countdown
function updateNextRefresh() {
    const nextRefresh = document.getElementById('nextRefresh');
    const seconds = Math.ceil(refreshInterval / 1000);
    nextRefresh.textContent = `${seconds}s`;
}

// Start auto-refresh
function startAutoRefresh() {
    if (refreshTimer) clearInterval(refreshTimer);
    
    refreshTimer = setInterval(() => {
        refreshAll();
    }, refreshInterval);
}

// Format date/time for display
function formatDateTime(isoString) {
    if (!isoString) return '-';
    try {
        const date = new Date(isoString);
        return date.toLocaleString();
    } catch (error) {
        return '-';
    }
}

// Save state
async function saveState() {
    if (!confirm('Are you sure you want to save the current state?')) {
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/state/save`, {
            method: 'POST'
        });
        const result = await response.json();
        
        if (result.success) {
            alert('State saved successfully!');
            await refreshAll();
        } else {
            alert(`Failed to save state: ${result.message}`);
        }
    } catch (error) {
        console.error('Error saving state:', error);
        alert('Failed to save state. Check console for details.');
    }
}

// Reload state
async function reloadState() {
    if (!confirm('Are you sure you want to reload the state? This will discard any unsaved changes.')) {
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/state/reload`, {
            method: 'POST'
        });
        const result = await response.json();
        
        if (result.success) {
            alert('State reloaded successfully!');
            await refreshAll();
        } else {
            alert(`Failed to reload state: ${result.message}`);
        }
    } catch (error) {
        console.error('Error reloading state:', error);
        alert('Failed to reload state. Check console for details.');
    }
}

// Terminal functionality
let terminalInterval = null;
let lastLogTimestamp = 0;

async function startTerminal() {
    if (terminalInterval) return;
    
    const terminalOutput = document.getElementById('terminalOutput');
    terminalOutput.innerHTML = '<div class="log-entry">Connecting to server logs...</div>';
    
    // Fetch logs periodically
    await fetchLogs();
    terminalInterval = setInterval(fetchLogs, 3000);
}

function stopTerminal() {
    if (terminalInterval) {
        clearInterval(terminalInterval);
        terminalInterval = null;
    }
}

async function fetchLogs() {
    try {
        const response = await fetch(`${API_BASE}/logs?since=${lastLogTimestamp}`);
        if (!response.ok) throw new Error('Failed to fetch logs');
        const data = await response.json();
        
        if (data.logs && data.logs.length > 0) {
            const terminalOutput = document.getElementById('terminalOutput');
            if (!terminalOutput) return;
            
            // Clear the "connecting" message if it exists
            if (terminalOutput.querySelector('.log-entry')?.textContent === 'Connecting to server logs...') {
                terminalOutput.innerHTML = '';
            }
            
            // Add new log entries
            data.logs.forEach(log => {
                addLogEntry(log.level || 'info', `[${new Date(log.timestamp).toISOString()}] ${log.message}`);
                // Update last timestamp
                if (log.timestamp > lastLogTimestamp) {
                    lastLogTimestamp = log.timestamp;
                }
            });
        }
    } catch (error) {
        console.error('Error fetching logs:', error);
        addLogEntry('error', 'Failed to fetch logs');
    }
}

function addLogEntry(level, message) {
    const terminalOutput = document.getElementById('terminalOutput');
    if (!terminalOutput) return;
    
    const logEntry = document.createElement('div');
    logEntry.className = `log-entry ${level}`;
    logEntry.textContent = message;
    terminalOutput.appendChild(logEntry);
    
    // Keep only last 100 entries to prevent memory issues
    while (terminalOutput.children.length > 100) {
        terminalOutput.removeChild(terminalOutput.firstChild);
    }
    
    if (autoScrollEnabled) {
        terminalOutput.scrollTop = terminalOutput.scrollHeight;
    }
}

function clearTerminal() {
    const terminalOutput = document.getElementById('terminalOutput');
    if (terminalOutput) {
        terminalOutput.innerHTML = '<div class="log-entry">Terminal cleared</div>';
    }
}

function toggleAutoScroll() {
    autoScrollEnabled = !autoScrollEnabled;
    const button = event.target;
    button.textContent = autoScrollEnabled ? 'Auto Scroll' : 'Scroll Off';
}

// Start the panel when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
} else {
    init();
}
