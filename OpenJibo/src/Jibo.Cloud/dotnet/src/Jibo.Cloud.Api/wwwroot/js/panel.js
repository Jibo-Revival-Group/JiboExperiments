const API_BASE = '/api/panel';
const OOBE_API_BASE = ''; // OOBE API endpoints are at root
let refreshInterval = 5000; // Default 5 seconds
let refreshTimer = null;
let isConnected = false;
let autoScrollEnabled = true;
let currentTab = 'dashboard';

// OOBE state
let oobeAuthToken = null;
let oobeSetupToken = null;
let oobeStatusInterval = null;

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

// OOBE Functions
async function oobeLogin() {
    const email = document.getElementById('oobe-email').value;
    const password = document.getElementById('oobe-password').value;

    try {
        const response = await fetch(`${OOBE_API_BASE}/api/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email, password })
        });

        const data = await response.json();

        if (response.ok) {
            oobeAuthToken = data.token;
            localStorage.setItem('oobeAuthToken', oobeAuthToken);
            showOobeDashboard();
        } else {
            alert(data.error || 'Login failed');
        }
    } catch (error) {
        console.error('OOBE login error:', error);
        alert('Login failed. Check console for details.');
    }
}

async function oobeSignup() {
    const email = document.getElementById('oobe-email').value;
    const password = document.getElementById('oobe-password').value;
    const firstName = document.getElementById('oobe-firstname').value;
    const lastName = document.getElementById('oobe-lastname').value;

    try {
        const response = await fetch(`${OOBE_API_BASE}/api/signup`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email, password, firstName, lastName })
        });

        const data = await response.json();

        if (response.ok) {
            oobeAuthToken = data.token;
            localStorage.setItem('oobeAuthToken', oobeAuthToken);
            showOobeDashboard();
        } else {
            alert(data.error || 'Signup failed');
        }
    } catch (error) {
        console.error('OOBE signup error:', error);
        alert('Signup failed. Check console for details.');
    }
}

function showOobeDashboard() {
    document.getElementById('oobe-auth-section').style.display = 'none';
    document.getElementById('oobe-dashboard-section').style.display = 'block';
}

function oobeLogout() {
    oobeAuthToken = null;
    oobeSetupToken = null;
    localStorage.removeItem('oobeAuthToken');
    document.getElementById('oobe-auth-section').style.display = 'block';
    document.getElementById('oobe-dashboard-section').style.display = 'none';
    document.getElementById('oobe-qr-section').style.display = 'none';
}

async function generateOobeQr() {
    const ssid = document.getElementById('oobe-ssid').value;
    const password = document.getElementById('oobe-wifi-password').value;
    const staticIP = document.getElementById('oobe-static-ip').value || null;
    const netmask = document.getElementById('oobe-netmask').value || null;
    const gateway = document.getElementById('oobe-gateway').value || null;
    const dns1 = document.getElementById('oobe-dns1').value || null;
    const dns2 = document.getElementById('oobe-dns2').value || null;

    if (!ssid || !password) {
        alert('SSID and password are required');
        return;
    }

    try {
        const response = await fetch(`${OOBE_API_BASE}/api/robots/setup`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${oobeAuthToken}`
            },
            body: JSON.stringify({
                ssid,
                password,
                staticIP,
                netmask,
                gateway,
                dns1,
                dns2
            })
        });

        const data = await response.json();

        if (response.ok) {
            oobeSetupToken = data.token;
            displayOobeQrCodes(data.qr.codes);
            document.getElementById('oobe-dashboard-section').style.display = 'none';
            document.getElementById('oobe-qr-section').style.display = 'block';
            startOobeStatusPolling(data.token);
        } else {
            alert('Failed to generate QR code');
        }
    } catch (error) {
        console.error('OOBE QR generation error:', error);
        alert('Failed to generate QR code. Check console for details.');
    }
}

function displayOobeQrCodes(codes) {
    const container = document.getElementById('qr-codes-container');
    container.innerHTML = codes.map((code, index) => `
        <div class="qr-code-item">
            <p class="qr-label">QR Code ${index + 1} of ${codes.length}</p>
            <div id="oobe-qr-${index}" class="qr-canvas"></div>
        </div>
    `).join('');

    codes.forEach((code, index) => {
        const qrElement = document.getElementById(`oobe-qr-${index}`);

        if (typeof QRCode !== 'undefined') {
            QRCode.toCanvas(qrElement, code, {
                width: 256,
                margin: 2,
                color: {
                    dark: '#000000',
                    light: '#ffffff'
                }
            }, function(error) {
                if (error) {
                    console.error('QR code generation error:', error);
                    qrElement.innerHTML = `<pre style="font-size: 10px; word-break: break-all; background: white; padding: 10px; border: 1px solid #ccc;">${code}</pre>`;
                }
            });
        } else {
            qrElement.innerHTML = `<pre style="font-size: 10px; word-break: break-all; background: white; padding: 10px; border: 1px solid #ccc;">${code}</pre>`;
        }
    });
}

function startOobeStatusPolling(token) {
    if (oobeStatusInterval) {
        clearInterval(oobeStatusInterval);
    }

    oobeStatusInterval = setInterval(async () => {
        try {
            const response = await fetch(`${OOBE_API_BASE}/api/robots/setup/${token}/status`);
            const data = await response.json();

            if (data.complete) {
                clearInterval(oobeStatusInterval);
                document.getElementById('oobe-setup-status').textContent = 'Setup Complete!';
                document.getElementById('oobe-setup-status').style.color = 'green';
            }
        } catch (error) {
            console.error('OOBE status polling error:', error);
        }
    }, 3000);
}

function backToOobeDashboard() {
    if (oobeStatusInterval) {
        clearInterval(oobeStatusInterval);
    }
    document.getElementById('oobe-qr-section').style.display = 'none';
    document.getElementById('oobe-dashboard-section').style.display = 'block';
    document.getElementById('oobe-setup-status').textContent = 'Waiting for robot to scan...';
    document.getElementById('oobe-setup-status').style.color = '';
}

// Check for existing OOBE auth on page load
function checkOobeAuth() {
    const savedToken = localStorage.getItem('oobeAuthToken');
    if (savedToken) {
        oobeAuthToken = savedToken;
        showOobeDashboard();
    }
}

// Start the panel when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        init();
        checkOobeAuth();
    });
} else {
    init();
    checkOobeAuth();
}
