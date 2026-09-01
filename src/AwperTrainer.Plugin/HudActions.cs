namespace AwperTrainer.Plugin;

internal static class HudActions
{
    public static readonly IReadOnlyDictionary<string, string> ConsoleCommands =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["start_button"] = "css_start",
            ["preview_button"] = "css_preview_toggle",
            ["abort_button"] = "css_abort",
            ["status_button"] = "css_status",
            ["list_button"] = "css_list",
            ["set_edit_button"] = "css_set_edit_anchor",
            ["set_player_button"] = "css_set_player_anchor",
            ["set_start_button"] = "css_set_bot_start",
            ["set_end_button"] = "css_set_bot_end",
            ["set_jiggle_button"] = "css_set_bot_jiggle",
            ["set_facing_button"] = "css_set_bot_facing",
            ["mode_direct_button"] = "css_mode 1",
            ["mode_jiggle_button"] = "css_mode 2",
            ["speed_150_button"] = "css_speed 150",
            ["speed_180_button"] = "css_speed 180",
            ["speed_215_button"] = "css_speed 215",
            ["validate_button"] = "css_validate",
            ["save_quick_button"] = "css_save",
            ["map_dust2_button"] = "css_map de_dust2",
            ["map_inferno_button"] = "css_map de_inferno",
            ["map_mirage_button"] = "css_map de_mirage",
            ["map_anubis_button"] = "css_map de_anubis",
            ["map_ancient_button"] = "css_map de_ancient",
            ["map_nuke_button"] = "css_map de_nuke",
            ["map_cache_button"] = "css_map de_cache"
        };

    public const string LoadFirstButton = "load_first_button";
    public const string CloseButton = "close_button";
}
