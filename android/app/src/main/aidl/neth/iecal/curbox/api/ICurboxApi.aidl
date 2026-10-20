// Curbox's public API contract (written from its documented signatures, CURBOX_API.md v2). The package and
// method order must match Curbox's own file, since AIDL calls are dispatched by position.
package neth.iecal.curbox.api;

interface ICurboxApi {
    int apiVersion();
    boolean isGranted();
    String execute(String command, in Bundle args);
    String query(String state);
    String list(String kind);
}
