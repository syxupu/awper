import { CSPlayerPawn, Entity, Instance } from "cs_script/point_script";

const allowedButtons = new Set([
    "start_button", "preview_button", "abort_button", "status_button", "list_button",
    "load_first_button", "set_edit_button", "set_player_button", "set_start_button",
    "set_end_button", "set_jiggle_button", "set_facing_button", "mode_direct_button",
    "mode_jiggle_button", "speed_150_button", "speed_180_button", "speed_215_button",
    "validate_button", "save_quick_button", "map_dust2_button", "map_inferno_button",
    "map_mirage_button", "map_anubis_button", "map_ancient_button", "map_nuke_button",
    "map_cache_button", "close_button"
]);

const openSlots = new Set();
let hudLayout = null;

function getHudLayout() {
    if (!(hudLayout instanceof Entity) || !hudLayout.IsValid()) {
        hudLayout = Instance.FindEntitiesByName("awper_hud_layout")[0];
    }
    return hudLayout;
}

function setOpen(playerSlot, open) {
    const layout = getHudLayout();
    if (!(layout instanceof Entity) || !layout.IsValid()) return;
    layout.SetHasClassForPlayer(playerSlot, "menu", "Hidden", !open);
    layout.SetInputCaptureEnabled(playerSlot, open);
    if (open) openSlots.add(playerSlot);
    else openSlots.delete(playerSlot);
}

function slotFromInput(inputData) {
    const pawn = inputData.activator;
    if (!(pawn instanceof CSPlayerPawn)) return -1;
    const controller = pawn.GetPlayerController();
    return controller === undefined ? -1 : controller.GetPlayerSlot();
}

function closeAll() {
    for (const playerSlot of Array.from(openSlots)) setOpen(playerSlot, false);
}

Instance.OnScriptInput("ToggleMenu", (inputData) => {
    const playerSlot = slotFromInput(inputData);
    if (playerSlot >= 0) setOpen(playerSlot, !openSlots.has(playerSlot));
});

Instance.OnScriptInput("CloseMenu", (inputData) => {
    const playerSlot = slotFromInput(inputData);
    if (playerSlot >= 0) setOpen(playerSlot, false);
});

Instance.OnScriptInput("CloseAll", closeAll);
Instance.OnScriptInput("Probe", () => Instance.ServerCommand("css_hud_ready"));

Instance.OnCustomHudClicked((event) => {
    if (event.layout !== getHudLayout() || !allowedButtons.has(event.buttonId)) return;
    const playerSlot = event.player.GetPlayerSlot();
    setOpen(playerSlot, false);
    Instance.ServerCommand(`css_hud_action ${playerSlot} ${event.buttonId}`);
});

Instance.OnPlayerDisconnect(({ playerSlot }) => setOpen(playerSlot, false));
Instance.OnPlayerReset(({ player }) => setOpen(player.GetOriginalPlayerController().GetPlayerSlot(), false));
Instance.OnRoundStart(closeAll);
Instance.OnRoundEnd(closeAll);
