// What the shell runs in every page of its site, once per page, when the page has loaded.
//
// A link with a `download` attribute to a file the page made itself (a blob: or data: address) is
// saved through the shell: a WebView saves no such link, and nothing outside the page can read one.
// The click is taken before the page's default runs, the bytes are read here while the address still
// resolves, and they go to the shell as base64 with the name the link gave (TksShell.saveFile). Every
// other link is the page's own.
(function () {
  if (window.__tksShellPage || !window.TksShell || !window.TksShell.saveFile) return;
  window.__tksShellPage = true;

  function save(link) {
    var name = link.getAttribute("download") || "download";
    fetch(link.href).then(function (response) { return response.blob(); }).then(function (blob) {
      var reader = new FileReader();
      reader.onload = function () {
        var data = String(reader.result);
        window.TksShell.saveFile(name, blob.type || "", data.slice(data.indexOf(",") + 1));
      };
      reader.readAsDataURL(blob);
    });
  }

  document.addEventListener("click", function (event) {
    var link = event.target && event.target.closest ? event.target.closest("a[download]") : null;
    if (!link || !/^(blob|data):/.test(link.href)) return;
    event.preventDefault();
    save(link);
  }, true);
})();
