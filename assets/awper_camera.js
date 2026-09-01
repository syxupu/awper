import { Instance } from "cs_script/point_script";

function requirePawn(inputData) {
    const pawn = inputData.activator;
    if (!pawn) {
        Instance.Msg("[AWPER] Camera bridge input has no player Pawn activator.");
        return undefined;
    }
    return pawn;
}

Instance.OnScriptInput("prepare", (inputData) => {
    const pawn = requirePawn(inputData);
    if (!pawn) return;
    const camera = pawn.GetCamera();
    camera.SetEnabled(false);
    camera.SetIsControllingAngles(true);
});

Instance.OnScriptInput("enable", (inputData) => {
    const pawn = requirePawn(inputData);
    if (!pawn) return;
    const camera = pawn.GetCamera();
    camera.SetIsControllingAngles(true);
    camera.SetEnabled(true);
});

Instance.OnScriptInput("disable", (inputData) => {
    const pawn = requirePawn(inputData);
    if (!pawn) return;
    pawn.GetCamera().SetEnabled(false);
});
