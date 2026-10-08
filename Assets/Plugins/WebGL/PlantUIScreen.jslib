// Plant UI: CSS height of the Unity <canvas>, so Unity can turn CSS pixels (React layout) into its own pixels.
// Called from PlantUI.cs.
mergeInto(LibraryManager.library, {
  PlantUI_CanvasCssHeight: function () {
    var canvas = (typeof Module !== "undefined" && Module.canvas) ? Module.canvas : document.querySelector("canvas");
    return canvas ? canvas.clientHeight : 0;
  }
});
