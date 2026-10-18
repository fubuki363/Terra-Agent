// Apply the saved appearance before any page content is painted.
(() => {
  const media = matchMedia("(prefers-color-scheme: dark)");
  let mode = localStorage.getItem("terra-theme") || "system";
  function apply(value) {
    mode = ["light", "dark", "system"].includes(value) ? value : "system";
    const dark = mode === "dark" || (mode === "system" && media.matches);
    document.body.toggleAttribute("data-ds-dark-theme", dark);
    document.documentElement.style.colorScheme = dark ? "dark" : "light";
    localStorage.setItem("terra-theme", mode);
    const button = document.getElementById("theme");
    if (button) {
      button.title = dark ? "切换为浅色风格" : "切换为夜间风格";
      button.setAttribute("aria-label", button.title);
      button.setAttribute("aria-pressed", String(dark));
    }
    window.dispatchEvent(
      new CustomEvent("terra-appearance", { detail: { mode, dark } }),
    );
    return dark;
  }
  window.terraTheme = {
    apply,
    get mode() {
      return mode;
    },
    get dark() {
      return document.body.hasAttribute("data-ds-dark-theme");
    },
  };
  media.addEventListener("change", () => {
    if (mode === "system") apply(mode);
  });
  apply(mode);
})();
