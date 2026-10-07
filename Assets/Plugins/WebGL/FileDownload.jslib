// Saves bytes made in Unity (e.g. the maintenance report .xlsx) as a browser download.
// Called from FileDownload.cs.
mergeInto(LibraryManager.library, {
  FileDownload_Save: function (namePtr, dataPtr, length, mimePtr) {
    var name = UTF8ToString(namePtr);
    var mime = UTF8ToString(mimePtr) || "application/octet-stream";
    var bytes = HEAPU8.slice(dataPtr, dataPtr + length); // copy: the Unity heap can move

    var blob = new Blob([bytes], { type: mime });
    var url = URL.createObjectURL(blob);
    var link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.style.display = "none";
    document.body.appendChild(link);
    link.click();
    setTimeout(function () {
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    }, 1500);
  }
});
