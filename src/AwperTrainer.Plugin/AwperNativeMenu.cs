using AwperTrainer.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;

namespace AwperTrainer.Plugin;

internal sealed class AwperNativeMenu(
    BasePlugin plugin,
    Func<IReadOnlyList<string>> profileNames,
    Func<IReadOnlyCollection<string>> allowedMaps)
{
    public bool Toggle(CCSPlayerController player)
    {
        if (MenuManager.GetActiveMenu(player) is not null)
        {
            MenuManager.CloseActiveMenu(player);
            return false;
        }

        OpenMain(player);
        return true;
    }

    public void Close(CCSPlayerController player)
        => MenuManager.CloseActiveMenu(player);

    public void CloseAll()
    {
        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true, IsBot: false }))
            Close(player);
    }

    private void OpenMain(CCSPlayerController player)
    {
        var menu = CreateMenu("AWPER 训练器 | F5 关闭");
        AddCommand(menu, "开始一轮训练", "css_start");
        AddCommand(menu, "切换摄像机预览", "css_preview_toggle");
        menu.AddMenuOption("加载本地图配置...", (target, _) => OpenProfiles(target));
        AddCommand(menu, "显示当前状态", "css_status");
        menu.AddMenuOption("设置点位 / 模式 / 速度...", (target, _) => OpenSetup(target));
        menu.AddMenuOption("切换训练地图...", (target, _) => OpenMaps(target));
        AddCommand(menu, "中止训练并恢复", "css_abort");
        Open(player, menu);
    }

    private void OpenProfiles(CCSPlayerController player)
    {
        var menu = CreateMenu("选择本地图配置");
        var names = profileNames();
        if (names.Count == 0)
            menu.AddMenuOption("当前地图没有配置", (_, _) => { }, disabled: true);
        else
            foreach (var name in names)
                AddCommand(menu, name, $"css_load {name}");
        menu.AddMenuOption("返回主菜单", (target, _) => OpenMain(target));
        Open(player, menu);
    }

    private void OpenSetup(CCSPlayerController player)
    {
        var menu = CreateMenu("点位 / 模式 / 速度设置");
        menu.AddMenuOption("先在聊天输入 !edit <名称>", (_, _) => { }, disabled: true);
        AddCommand(menu, "1. 记录编辑入口 EditAnchor", "css_set_edit_anchor");
        AddCommand(menu, "2. 记录玩家训练点 PlayerAnchor", "css_set_player_anchor");
        AddCommand(menu, "3. 记录 Bot 起点", "css_set_bot_start");
        AddCommand(menu, "4. 记录 Bot 终点", "css_set_bot_end");
        AddCommand(menu, "5. 记录 Bot 急停点", "css_set_bot_jiggle");
        AddCommand(menu, "6. 记录 Bot 面向", "css_set_bot_facing");
        AddCommand(menu, "直拉模式", "css_mode 1");
        AddCommand(menu, "急停模式", "css_mode 2");
        AddCommand(menu, "AK 正常持枪速度：215", "css_speed 215");
        AddCommand(menu, "中速：180", "css_speed 180");
        AddCommand(menu, "慢速：150", "css_speed 150");
        AddCommand(menu, "验证当前配置", "css_validate");
        AddCommand(menu, "保存当前命名轨道", "css_save");
        menu.AddMenuOption("返回主菜单", (target, _) => OpenMain(target));
        Open(player, menu);
    }

    private void OpenMaps(CCSPlayerController player)
    {
        var menu = CreateMenu("选择训练地图");
        var current = MapPolicy.Normalize(Server.MapName);
        foreach (var map in allowedMaps())
        {
            var label = string.Equals(map, current, StringComparison.OrdinalIgnoreCase)
                ? $"{map[3..]}（当前）"
                : map[3..];
            AddCommand(menu, label, $"css_map {map}", disabled: string.Equals(map, current, StringComparison.OrdinalIgnoreCase));
        }
        menu.AddMenuOption("返回主菜单", (target, _) => OpenMain(target));
        Open(player, menu);
    }

    private CenterHtmlMenu CreateMenu(string title)
        => new(title, plugin)
        {
            TitleColor = "#ffb000",
            EnabledColor = "#f2f2f2",
            DisabledColor = "#777777",
            PrevPageColor = "#8ecae6",
            NextPageColor = "#8ecae6",
            CloseColor = "#ff6b6b",
            ExitButton = true,
            PostSelectAction = PostSelectAction.Nothing
        };

    private static void AddCommand(CenterHtmlMenu menu, string label, string command, bool disabled = false)
        => menu.AddMenuOption(label, (player, _) =>
        {
            MenuManager.CloseActiveMenu(player);
            player.ExecuteClientCommandFromServer(command);
        }, disabled);

    private void Open(CCSPlayerController player, CenterHtmlMenu menu)
        => MenuManager.OpenCenterHtmlMenu(plugin, player, menu);
}
