function initStopAllButtonSync() {
    document.body.addEventListener('htmx:afterSwap', function (evt) {
        if (evt.detail.target && evt.detail.target.id === 'running-count') {
            let count = parseInt(evt.detail.target.innerText) || 0;
            document.getElementById('stop-all-btn').disabled = count === 0;
        }

        // When a status cell is swapped, check if the process is no longer running
        if (evt.detail.target && evt.detail.target.id && evt.detail.target.id.startsWith('status-')) {
            let projectId = evt.detail.target.id.substring('status-'.length);
            let badge = evt.detail.target.querySelector('.badge');
            let isRunning = badge && badge.textContent.trim() === 'running';
            let group = document.getElementById('actions-' + projectId);

            if (!isRunning && group && group.hasAttribute('data-original-html')) {
                restoreActions(projectId);
            }
        }
    });
}

initStopAllButtonSync();
