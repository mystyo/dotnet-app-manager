function initProjectFilter() {
    document.getElementById('project-search').addEventListener('input', function () {
        var filter = this.value.toLowerCase();
        document.querySelectorAll('.project-row').forEach(function (row) {
            var name = row.getAttribute('data-project-name');
            row.style.display = name.includes(filter) ? '' : 'none';
        });
    });

    document.getElementById('select-all-checkbox').addEventListener('change', function () {
        var checked = this.checked;
        document.querySelectorAll('.project-select-checkbox').forEach(function (cb) {
            cb.checked = checked;
        });
    });
}

initProjectFilter();
