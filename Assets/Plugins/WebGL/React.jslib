// Unity -> React. One function per event, called from CommunicationManager.cs (HandleXxx_Extern).
// React listens with react-unity-webgl:  addEventListener("handleXxx", (data) => ...)
// Every call is guarded: outside React (plain Unity page, Unity Play, http-server test) there is no
// window.dispatchReactUnityEvent, and calling it would crash the whole app.
// Generated from the same table as CommunicationManager.cs – keep both in sync.
mergeInto(LibraryManager.library, {
  handleError: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleError", UTF8ToString(data));
  },
  handleSceneLoadFailed: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleSceneLoadFailed", UTF8ToString(data));
  },
  handleUnityReady: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleUnityReady", UTF8ToString(data));
  },
  handleCursorLockChanged: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleCursorLockChanged", UTF8ToString(data));
  },
  handleWorkerTransform: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleWorkerTransform", UTF8ToString(data));
  },
  handleSceneLoading: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleSceneLoading", UTF8ToString(data));
  },
  handleSceneDownloadProgress: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleSceneDownloadProgress", UTF8ToString(data));
  },
  handleSceneLoaded: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleSceneLoaded", UTF8ToString(data));
  },
  handleScenePreloaded: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleScenePreloaded", UTF8ToString(data));
  },
  handleSceneDownloadSize: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleSceneDownloadSize", UTF8ToString(data));
  },
  handleSceneTriggerEntered: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleSceneTriggerEntered", UTF8ToString(data));
  },
  handleSceneTriggerExited: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleSceneTriggerExited", UTF8ToString(data));
  },
  handleCameraModeChanged: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleCameraModeChanged", UTF8ToString(data));
  },
  handleOverviewObjectSelected: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleOverviewObjectSelected", UTF8ToString(data));
  },
  handleOverviewObjectDeselected: function () {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleOverviewObjectDeselected");
  },
  handleEquipmentInRange: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleEquipmentInRange", UTF8ToString(data));
  },
  handleEquipmentOutOfRange: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleEquipmentOutOfRange", UTF8ToString(data));
  },
  handleTurbineInRange: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleTurbineInRange", UTF8ToString(data));
  },
  handleTurbineOutOfRange: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleTurbineOutOfRange", UTF8ToString(data));
  },
  handleBoilerInRange: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleBoilerInRange", UTF8ToString(data));
  },
  handleBoilerOutOfRange: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleBoilerOutOfRange", UTF8ToString(data));
  },
  handleEquipmentState: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleEquipmentState", UTF8ToString(data));
  },
  handlePartHover: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handlePartHover", UTF8ToString(data));
  },
  handlePartHoverEnd: function () {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handlePartHoverEnd");
  },
  handlePartSelected: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handlePartSelected", UTF8ToString(data));
  },
  handlePartDeselected: function () {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handlePartDeselected");
  },
  handleTurbineData: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleTurbineData", UTF8ToString(data));
  },
  handleTurbineList: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleTurbineList", UTF8ToString(data));
  },
  handleBoilerDashboardOpened: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleBoilerDashboardOpened", UTF8ToString(data));
  },
  handleBoilerDashboard: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleBoilerDashboard", UTF8ToString(data));
  },
  handleBoilerDashboardClosed: function () {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleBoilerDashboardClosed");
  },
  handleElectricalPanelOpened: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleElectricalPanelOpened", UTF8ToString(data));
  },
  handleElectricalValues: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleElectricalValues", UTF8ToString(data));
  },
  handleTimeOfDayChanged: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleTimeOfDayChanged", UTF8ToString(data));
  },
  handleFlowViewChanged: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleFlowViewChanged", UTF8ToString(data));
  },
  handleControlRoomInfoEntered: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleControlRoomInfoEntered", UTF8ToString(data));
  },
  handleControlRoomInfoExited: function (data) {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleControlRoomInfoExited", UTF8ToString(data));
  },
  handleElectricalPanelClosed: function () {
    if (typeof window.dispatchReactUnityEvent === "function") window.dispatchReactUnityEvent("handleElectricalPanelClosed");
  },
});
