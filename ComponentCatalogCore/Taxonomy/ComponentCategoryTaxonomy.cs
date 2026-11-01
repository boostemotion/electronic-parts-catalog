namespace ComponentCatalogCore.Taxonomy;

public static class ComponentCategoryTaxonomy
{
    public const string Version = ProductVersion.TaxonomyVersion;
    public const string OtherMajorCategoryId = "other";
    public const string UncategorizedSubCategoryId = "uncategorized";

    private static readonly IReadOnlyList<ComponentCategoryDefinition> Categories = new[]
    {
        Major("passives", "电容 / 电阻 / 电感", "mlcc:贴片电容(MLCC)", "through-hole-capacitor:直插电容", "aluminum-electrolytic-capacitor:铝电解电容", "tantalum-capacitor:钽电容", "solid-capacitor:固态电容", "safety-capacitor:安规电容", "film-capacitor:薄膜电容", "chip-resistor:贴片电阻", "through-hole-resistor:插件电阻", "current-sense-resistor:电流采样电阻", "resistor-array:排阻", "power-resistor:功率电阻", "potentiometer:可调电阻/电位器", "chip-inductor:贴片电感", "power-inductor:功率电感", "through-hole-inductor:插件电感", "transformer:变压器", "ferrite-bead:磁珠", "passives-other:其他阻容感"),
        Major("mcu-logic", "微控制器 / 逻辑器件", "mcu-mpu-soc:单片机/微控制器", "dsp-dsc:DSP/DSC", "cpld-fpga:CPLD/FPGA", "logic-gate:逻辑门", "logic-buffer-transceiver:缓冲器/驱动器/收发器", "logic-register-latch:寄存器/锁存器/触发器", "logic-switch-mux:逻辑开关/多路复用器", "logic-other:其他逻辑器件"),
        Major("diodes-transistors", "二极管 / 晶体管", "zener-diode:稳压二极管", "schottky-diode:肖特基二极管", "rectifier-diode:整流/通用二极管", "switching-diode:开关二极管", "bjt:三极管(BJT)", "mosfet:MOSFET", "igbt:IGBT", "thyristor:晶闸管/可控硅", "transistors-other:其他晶体管"),
        Major("protection-power", "板级电路保护 / 电源管理", "ldo:线性稳压器(LDO)", "dc-dc:DC-DC电源芯片", "ac-dc:AC-DC控制器", "battery-management:电池管理", "quick-charge-controller:快充协议芯片", "voltage-reference:电压基准", "power-switch:功率电子开关", "motor-gate-driver:电机/栅极驱动", "tvs-esd:TVS/ESD保护", "fuse-protection:保险丝/板级保护", "power-other:其他电源管理"),
        Major("interface-timing", "接口芯片 / 时钟和计时", "rs485-rs422:RS-485/RS-422", "rs232:RS-232", "can-interface:CAN接口", "usb-interface:USB接口芯片", "ethernet-interface:以太网接口", "lin-interface:LIN接口", "other-interface:其他通信接口", "rtc:实时时钟(RTC)", "timer:定时器", "clock-generator:时钟发生/缓冲"),
        Major("connectors-switches", "连接器 / 端子 / 开关", "pin-header:排针", "female-header:排母", "wire-to-board:线对板连接器", "usb-connector:USB连接器", "dc-power-connector:DC电源连接器", "terminal-block:接线端子", "board-to-board:板对板连接器", "rf-connector:射频连接器", "connector-other:其他连接器", "tactile-switch:轻触开关", "slide-switch:滑动开关", "dip-switch:拨码开关", "switch-other:其他开关"),
        Major("data-rf", "数据转换芯片 / 射频无线", "adc:ADC", "dac:DAC", "data-converter-other:其他数据转换芯片", "rf-transceiver:射频收发器", "rf-amplifier:射频放大器", "rf-switch:射频开关", "antenna:天线", "rf-other:其他射频无线"),
        Major("opto-display", "光电器件 / 显示模块", "led:LED", "rgb-led:RGB LED", "photodiode:光电二极管", "phototransistor:光电三极管", "opto-sensor:光电传感器", "lcd:LCD显示屏", "oled:OLED显示屏", "led-display:LED显示模块", "display-other:其他显示器件"),
        Major("filters-oscillators", "滤波器 / 晶体 / 振荡器", "common-mode-filter:共模滤波器", "emi-filter:EMI滤波器", "saw-filter:SAW滤波器", "rf-filter:射频滤波器", "passive-crystal:无源晶体", "active-oscillator:有源振荡器", "resonator:谐振器", "oscillator-other:其他晶振/振荡器"),
        Major("memory-sensors-isolation", "存储器 / 传感器 / 隔离器件", "eeprom:EEPROM", "nor-flash:NOR Flash", "nand-flash:NAND Flash", "ram:RAM", "memory-other:其他存储器", "temperature-sensor:温度传感器", "pressure-sensor:压力传感器", "motion-sensor:运动传感器", "magnetic-sensor:磁传感器", "sensor-other:其他传感器", "optocoupler:光耦", "digital-isolator:数字隔离器", "isolation-other:其他隔离器件"),
        Major("relays-audio-motors", "继电器 / 蜂鸣器 / 马达", "power-relay:功率继电器", "signal-relay:信号继电器", "buzzer:蜂鸣器", "speaker:扬声器", "microphone:麦克风", "vibration-motor:振动马达", "motor:马达", "group-other:其他继电器/音频/马达"),
        Major("modules-communication", "功能模块 / 通信模块", "power-module:电源模块", "sensor-module:传感器模块", "motor-module:电机模块", "bluetooth-module:蓝牙模块", "wifi-module:Wi-Fi模块", "lora-module:LoRa模块", "cellular-module:蜂窝通信模块", "gnss-module:GNSS模块", "communication-module-other:其他通信模块", "function-module-other:其他功能模块"),
        Major("amplifiers-development", "放大器 / 开发板", "op-amp:运算放大器", "comparator:比较器", "instrumentation-amplifier:仪表放大器", "audio-amplifier:音频放大器", "amplifier-other:其他放大器", "development-board:开发板", "programmer-debugger:编程器/调试器", "development-other:其他开发工具"),
        Major("other", "其他", "uncategorized:未分类"),
    }.Select((category, index) => category with { Order = index + 1 }).ToList();

    private static readonly IReadOnlyDictionary<string, ComponentCategoryDefinition> MajorsById = Categories.ToDictionary(category => category.Id, StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, ComponentSubcategoryDefinition> SubcategoriesById = Categories.SelectMany(category => category.Subcategories).ToDictionary(category => category.Id, StringComparer.Ordinal);

    public static ComponentCategoryTaxonomyDefinition Definition { get; } = new(Version, Categories);
    public static IReadOnlyList<ComponentCategoryDefinition> MajorCategories => Categories;

    public static ComponentCategoryDefinition? FindMajor(string? majorCategoryId) =>
        !string.IsNullOrWhiteSpace(majorCategoryId) && MajorsById.TryGetValue(majorCategoryId.Trim(), out var major) ? major : null;

    public static ComponentSubcategoryDefinition? FindSubcategory(string? subCategoryId) =>
        !string.IsNullOrWhiteSpace(subCategoryId) && SubcategoriesById.TryGetValue(subCategoryId.Trim(), out var subcategory) ? subcategory : null;

    public static bool IsValidPair(string? majorCategoryId, string? subCategoryId) =>
        FindSubcategory(subCategoryId) is { } subcategory && string.Equals(subcategory.MajorCategoryId, majorCategoryId?.Trim(), StringComparison.Ordinal);

    public static ComponentCategoryAssignment GetAssignment(string majorCategoryId, string subCategoryId)
    {
        if (!IsValidPair(majorCategoryId, subCategoryId))
        {
            throw new ArgumentException("Unknown or incompatible category IDs.");
        }

        var major = MajorsById[majorCategoryId];
        var subcategory = SubcategoriesById[subCategoryId];
        return new ComponentCategoryAssignment(major.Id, subcategory.Id, major.Label, subcategory.Label);
    }

    public static ComponentCategoryAssignment GetUncategorizedAssignment() => GetAssignment(OtherMajorCategoryId, UncategorizedSubCategoryId);

    public static ComponentCategoryAssignment? ResolveLegacy(string? category, string? package = null, string? partNumber = null)
    {
        var normalized = (category ?? string.Empty).Trim();
        var part = (partNumber ?? string.Empty).Trim();
        var isSmdPackage = !string.IsNullOrWhiteSpace(package) && package.Trim().Any(char.IsDigit);

        return normalized switch
        {
            "贴片电容" or "电容" => GetAssignment("passives", "mlcc"),
            "贴片电阻" => GetAssignment("passives", "chip-resistor"),
            "电阻" when isSmdPackage => GetAssignment("passives", "chip-resistor"),
            "电阻" => GetAssignment("passives", "through-hole-resistor"),
            "贴片电感" when part.StartsWith("FXL0630-100-M", StringComparison.OrdinalIgnoreCase) => GetAssignment("passives", "power-inductor"),
            "贴片电感" or "电感" => GetAssignment("passives", "chip-inductor"),
            "微控制器(MCU)" or "MCU" or "集成电路" => GetAssignment("mcu-logic", "mcu-mpu-soc"),
            "二极管" => GetAssignment("diodes-transistors", "rectifier-diode"),
            "三极管" => GetAssignment("diodes-transistors", "bjt"),
            "电源" => GetAssignment("protection-power", "power-other"),
            "连接器" => GetAssignment("connectors-switches", "connector-other"),
            "晶振" => GetAssignment("filters-oscillators", "passive-crystal"),
            "其他" or "未分类" or "" => GetUncategorizedAssignment(),
            _ => null,
        };
    }

    private static ComponentCategoryDefinition Major(string id, string label, params string[] subcategoryDefinitions)
    {
        var subcategories = subcategoryDefinitions
            .Select((definition, index) => new { Parts = definition.Split(':', 2), Index = index })
            .Select(item => new ComponentSubcategoryDefinition(item.Parts[0], id, item.Parts[1], item.Index + 1))
            .ToList();
        return new ComponentCategoryDefinition(id, label, 0, subcategories);
    }
}
