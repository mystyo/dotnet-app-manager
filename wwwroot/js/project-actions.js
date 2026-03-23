function getSelectedConfiguration() {
    return document.getElementById('build-configuration').value;
}

function extractProcessId(html) {
    var match = html.match(/sse-connect="\/api\/process\/stream\/([^"]+)"/);
    return match ? match[1] : null;
}

function replaceActionsWithStop(projectId, processId) {
    var group = document.getElementById('actions-' + projectId);
    if (!group) return;
    group.setAttribute('data-original-html', group.innerHTML);
    group.innerHTML = '<button class="btn btn-sm btn-outline-danger" ' +
        'hx-post="/api/process/stop/' + processId + '" ' +
        'hx-target="#console-' + projectId + '" ' +
        'hx-swap="innerHTML">Stop</button>';
    htmx.process(group);
}

function restoreActions(projectId) {
    var group = document.getElementById('actions-' + projectId);
    if (!group) return;
    var original = group.getAttribute('data-original-html');
    if (original) {
        group.innerHTML = original;
        group.removeAttribute('data-original-html');
        htmx.process(group);
    }
}

function startProcessAndSwapActions(projectId, target, fetchPromise) {
    fetchPromise
        .then(function (r) { return r.text(); })
        .then(function (html) {
            var processId = extractProcessId(html);
            if (processId) {
                replaceActionsWithStop(projectId, processId);
            }
            document.querySelector(target).innerHTML = html;
            htmx.process(document.querySelector(target));
        });
}

function initProjectActionButtons() {
    document.body.addEventListener('click', function (e) {
        var btn = e.target.closest('.action-btn');
        if (!btn) return;

        var action = btn.getAttribute('data-action');
        var projectId = btn.getAttribute('data-project-id');
        var projectPath = btn.getAttribute('data-project-path');
        var target = btn.getAttribute('data-target');
        var config = getSelectedConfiguration();
        var buildDeps = document.getElementById('build-deps-checkbox').checked;
        var changesOnly = document.getElementById('changes-only-checkbox').checked;
        var deps = JSON.parse(btn.getAttribute('data-dependencies') || '[]');

        if (buildDeps && deps.length > 0) {
            startProcessAndSwapActions(projectId, target, fetch('/api/process/build-chain', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    projectId: projectId,
                    projectPath: decodeURIComponent(projectPath),
                    action: action,
                    configuration: config,
                    dependencyPaths: deps,
                    changesOnly: changesOnly
                })
            }));
        } else {
            var url = '/api/process/' + action + '?projectId=' + projectId + '&projectPath=' + projectPath + '&configuration=' + encodeURIComponent(config);
            startProcessAndSwapActions(projectId, target, fetch(url, { method: 'POST' }));
        }
    });
}

function initDependencyBuildButtons() {
    document.body.addEventListener('click', function (e) {
        var btn = e.target.closest('.dep-build-btn');
        if (!btn) return;

        var depPath = btn.getAttribute('data-dep-path');
        var ownerProjectId = btn.getAttribute('data-owner-project-id');
        var config = getSelectedConfiguration();
        var target = '#console-' + ownerProjectId;

        btn.disabled = true;

        var url = '/api/process/build?projectId=' + ownerProjectId + '&projectPath=' + encodeURIComponent(depPath) + '&configuration=' + encodeURIComponent(config);
        startProcessAndSwapActions(ownerProjectId, target, fetch(url, { method: 'POST' }));
    });
}

initProjectActionButtons();
initDependencyBuildButtons();
