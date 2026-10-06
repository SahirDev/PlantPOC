// Keyboard focus for the Unity canvas inside a React page.
//
// Unity only receives keys (WASD, Shift, Space, C ...) while its <canvas> has focus. The react-unity-webgl
// <Unity> component does not make the canvas focusable, so keys never arrived. This:
//   - makes the canvas focusable and focuses it once at start (WASD works without a click),
//   - focuses it whenever the 3D view is clicked,
//   - after a click on a React button / slider / empty area, gives the keys back to Unity,
//   - but never steals focus from a React text field (input, textarea, select, contenteditable),
//     so typing in React keeps working.
// Called from CommunicationManager (Awake, FocusUnity_Extern, SetKeyboardAutoFocus_Extern).
mergeInto(LibraryManager.library, {
  KeyboardFocus_Setup: function (autoFocus) {
    var canvas = Module["canvas"];
    if (!canvas) return;

    var state = window.__unityKeyboardFocus || (window.__unityKeyboardFocus = { installed: false, auto: true });
    state.auto = !!autoFocus;
    if (state.installed) return;
    state.installed = true;

    if (!canvas.hasAttribute("tabindex")) canvas.setAttribute("tabindex", "-1");
    canvas.style.outline = "none";

    var isTextEntry = function (el) {
      if (!el) return false;
      if (el.isContentEditable) return true;
      var tag = el.tagName;
      if (tag === "TEXTAREA" || tag === "SELECT") return true;
      if (tag !== "INPUT") return false;
      var type = (el.getAttribute("type") || "text").toLowerCase();
      return ["button", "checkbox", "radio", "range", "submit", "reset", "color", "file", "image"].indexOf(type) < 0;
    };

    var focusCanvas = function () {
      if (document.activeElement === canvas || isTextEntry(document.activeElement)) return;
      try { canvas.focus({ preventScroll: true }); } catch (e) { canvas.focus(); }
    };
    state.focus = function () {
      try { canvas.focus({ preventScroll: true }); } catch (e) { canvas.focus(); }
    };

    // Click on the 3D view -> keys to Unity.
    canvas.addEventListener("pointerdown", function () { state.focus(); }, true);

    // Click anywhere else (React button, slider, empty area) -> keys back to Unity, unless a text field got focus.
    document.addEventListener("pointerup", function () {
      if (!state.auto) return;
      setTimeout(focusCanvas, 0);
    }, true);

    if (state.auto) setTimeout(focusCanvas, 0);
  },

  KeyboardFocus_Focus: function () {
    var state = window.__unityKeyboardFocus;
    if (state && state.focus) state.focus();
  },

  KeyboardFocus_SetAuto: function (autoFocus) {
    var state = window.__unityKeyboardFocus;
    if (state) state.auto = !!autoFocus;
  },
});
