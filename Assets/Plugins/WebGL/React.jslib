// Unity -> React. One function per event, called from CommunicationManager.cs (HandleXxx_Extern).
// React listens with react-unity-webgl:  addEventListener("handleXxx", (data) => ...)
// Generated from the same table as CommunicationManager.cs – keep both in sync.
mergeInto(LibraryManager.library, {
  handleError: function (data) {
    window.dispatchReactUnityEvent("handleError", UTF8ToString(data));
  },
  handleSceneLoadFailed: function (data) {
    window.dispatchReactUnityEvent("handleSceneLoadFailed", UTF8ToString(data));
  },
  handleUnityReady: function (data) {
    window.dispatchReactUnityEvent("handleUnityReady", UTF8ToString(data));
  },
  handleCursorLockChanged: function (data) {
    window.dispatchReactUnityEvent("handleCursorLockChanged", UTF8ToString(data));
  },
  handleWorkerTransform: function (data) {
    window.dispatchReactUnityEvent("handleWorkerTransform", UTF8ToString(data));
  },
  handleSceneLoading: function (data) {
    window.dispatchReactUnityEvent("handleSceneLoading", UTF8ToString(data));
  },
  handleSceneDownloadProgress: function (data) {
    window.dispatchReactUnityEvent("handleSceneDownloadProgress", UTF8ToString(data));
  },
  handleSceneLoaded: function (data) {
    window.dispatchReactUnityEvent("handleSceneLoaded", UTF8ToString(data));
  },
  handleScenePreloaded: function (data) {
    window.dispatchReactUnityEvent("handleScenePreloaded", UTF8ToString(data));
  },
  handleSceneDownloadSize: function (data) {
    window.dispatchReactUnityEvent("handleSceneDownloadSize", UTF8ToString(data));
  },
  handleSceneTriggerEntered: function (data) {
    window.dispatchReactUnityEvent("handleSceneTriggerEntered", UTF8ToString(data));
  },
  handleSceneTriggerExited: function (data) {
    window.dispatchReactUnityEvent("handleSceneTriggerExited", UTF8ToString(data));
  },
  handleCameraModeChanged: function (data) {
    window.dispatchReactUnityEvent("handleCameraModeChanged", UTF8ToString(data));
  },
  handleOverviewObjectSelected: function (data) {
    window.dispatchReactUnityEvent("handleOverviewObjectSelected", UTF8ToString(data));
  },
  handleOverviewObjectDeselected: function () {
    window.dispatchReactUnityEvent("handleOverviewObjectDeselected");
  },
  handleEquipmentInRange: function (data) {
    window.dispatchReactUnityEvent("handleEquipmentInRange", UTF8ToString(data));
  },
  handleEquipmentOutOfRange: function (data) {
    window.dispatchReactUnityEvent("handleEquipmentOutOfRange", UTF8ToString(data));
  },
  handleEquipmentState: function (data) {
    window.dispatchReactUnityEvent("handleEquipmentState", UTF8ToString(data));
  },
  handlePartHover: function (data) {
    window.dispatchReactUnityEvent("handlePartHover", UTF8ToString(data));
  },
  handlePartHoverEnd: function () {
    window.dispatchReactUnityEvent("handlePartHoverEnd");
  },
  handlePartSelected: function (data) {
    window.dispatchReactUnityEvent("handlePartSelected", UTF8ToString(data));
  },
  handlePartDeselected: function () {
    window.dispatchReactUnityEvent("handlePartDeselected");
  },
  handleTurbineData: function (data) {
    window.dispatchReactUnityEvent("handleTurbineData", UTF8ToString(data));
  },
  handleTurbineList: function (data) {
    window.dispatchReactUnityEvent("handleTurbineList", UTF8ToString(data));
  },
  handleBoilerDashboardOpened: function (data) {
    window.dispatchReactUnityEvent("handleBoilerDashboardOpened", UTF8ToString(data));
  },
  handleBoilerDashboard: function (data) {
    window.dispatchReactUnityEvent("handleBoilerDashboard", UTF8ToString(data));
  },
  handleBoilerDashboardClosed: function () {
    window.dispatchReactUnityEvent("handleBoilerDashboardClosed");
  },
  handleElectricalPanelOpened: function (data) {
    window.dispatchReactUnityEvent("handleElectricalPanelOpened", UTF8ToString(data));
  },
  handleElectricalValues: function (data) {
    window.dispatchReactUnityEvent("handleElectricalValues", UTF8ToString(data));
  },
  handleElectricalPanelClosed: function () {
    window.dispatchReactUnityEvent("handleElectricalPanelClosed");
  },
});
