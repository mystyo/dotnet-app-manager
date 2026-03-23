function initBuildAllButton() {
    document.getElementById('build-all-btn').addEventListener('click', function () {
        var config = getSelectedConfiguration();
        var profile = document.getElementById('profile-select').value;
        var url = '/api/process/build-all?configuration=' + encodeURIComponent(config);
        if (profile) url += '&profile=' + encodeURIComponent(profile);
        fetch(url, { method: 'POST' })
            .then(function (r) { return r.text(); })
            .then(function (html) {
                var target = document.getElementById('console-build-all');
                target.innerHTML = html;
                htmx.process(target);
            });
    });
}

function initRunAllButton() {
    document.getElementById('run-all-btn').addEventListener('click', function () {
        var config = getSelectedConfiguration();
        var profile = document.getElementById('profile-select').value;
        var url = '/api/process/run-all?configuration=' + encodeURIComponent(config);
        if (profile) url += '&profile=' + encodeURIComponent(profile);
        var form = document.createElement('form');
        form.method = 'POST';
        form.action = url;
        document.body.appendChild(form);
        form.submit();
    });
}

function initRunSelectedButton() {
    document.getElementById('run-selected-btn').addEventListener('click', function () {
        var config = getSelectedConfiguration();
        var buildDeps = document.getElementById('build-deps-checkbox').checked;
        var selected = document.querySelectorAll('.project-select-checkbox:checked');
        if (selected.length === 0) return;
        selected.forEach(function (cb) {
            var projectId = cb.getAttribute('data-project-id');
            var projectPath = cb.getAttribute('data-project-path');
            var target = '#console-' + projectId;

            if (buildDeps) {
                var actionBtn = document.querySelector('.action-btn[data-action="run"][data-project-id="' + projectId + '"]');
                var deps = actionBtn ? JSON.parse(actionBtn.getAttribute('data-dependencies') || '[]') : [];

                if (deps.length > 0) {
                    startProcessAndSwapActions(projectId, target, fetch('/api/process/build-chain', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            projectId: projectId,
                            projectPath: decodeURIComponent(projectPath),
                            action: 'run',
                            configuration: config,
                            dependencyPaths: deps
                        })
                    }));
                    return;
                }
            }

            var url = '/api/process/run?projectId=' + projectId + '&projectPath=' + projectPath + '&configuration=' + encodeURIComponent(config);
            htmx.ajax('POST', url, { target: target, swap: 'innerHTML' });
        });
    });
}

initBuildAllButton();
initRunAllButton();
initRunSelectedButton();
