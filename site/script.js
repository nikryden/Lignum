// Lignum home page: theme toggle and latest-release download links. No dependencies.

(() => {
  const root = document.documentElement;
  const toggle = document.getElementById("themeToggle");

  const current = () =>
    root.dataset.theme || (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");

  toggle.addEventListener("click", () => {
    const next = current() === "dark" ? "light" : "dark";
    root.dataset.theme = next;
    try { localStorage.setItem("lignum-theme", next); } catch { /* private mode: not remembered */ }
  });

  // Point the download buttons straight at the latest release's files.
  // Works once the repository is public; until then the buttons open the Releases page.
  fetch("https://api.github.com/repos/nikryden/Lignum/releases/latest", {
    headers: { Accept: "application/vnd.github+json" },
  })
    .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
    .then((release) => {
      const assets = release.assets || [];
      const msi = assets.find((a) => a.name.toLowerCase().endsWith(".msi"));
      const exe = assets.find((a) => a.name.toLowerCase().endsWith(".exe"));
      if (msi) document.getElementById("downloadMsi").href = msi.browser_download_url;
      if (exe) document.getElementById("downloadExe").href = exe.browser_download_url;

      const date = new Date(release.published_at).toLocaleDateString(undefined, {
        year: "numeric", month: "long", day: "numeric",
      });
      document.getElementById("releaseInfo").textContent =
        `${release.tag_name} · released ${date} · Windows 10 and 11, x64. No .NET installation needed.`;
    })
    .catch(() => { /* keep the Releases page links */ });
})();
