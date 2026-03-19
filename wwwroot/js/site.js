// Auto-scroll console output when new SSE messages arrive
document.body.addEventListener("htmx:sseMessage", function (event) {
    var target = event.detail.elt;
    if (target && target.classList.contains("console-output")) {
        requestAnimationFrame(function () {
            target.scrollTop = target.scrollHeight;
        });
    }
});
