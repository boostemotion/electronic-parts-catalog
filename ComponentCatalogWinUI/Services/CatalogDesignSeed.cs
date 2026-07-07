using ComponentCatalogCore.Contracts;

namespace ComponentCatalogWinUI.Services;

public static class CatalogDesignSeed
{
    public static IReadOnlyList<ComponentRecord> CreateSampleComponents()
    {
        return
        [
            new ComponentRecord
            {
                Id = "mcu-stm32f103",
                Name = "STM32F103C8T6",
                PartNumber = "STM32F103C8T6",
                Category = "MCU",
                Package = "LQFP48",
                Aliases = ["BluePill MCU"],
                Keywords = ["stm32", "cortex-m3", "main-controller"],
                Attributes = new Dictionary<string, string>
                {
                    ["frequency"] = "72MHz",
                    ["flash"] = "64KB",
                    ["core"] = "ARM Cortex-M3",
                },
                Owned = true,
                Location = "主控盒 A2",
                Substitutes = [],
                Notes = "蓝板常用主控，适合小型控制板与调试板。",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "ldo-ams1117",
                Name = "AMS1117-3.3",
                PartNumber = "AMS1117-3.3",
                Category = "电源",
                Package = "SOT223",
                Aliases = ["1117 LDO"],
                Keywords = ["ldo", "3.3v", "regulator"],
                Attributes = new Dictionary<string, string>
                {
                    ["voltage"] = "3.3V",
                    ["current"] = "800mA",
                },
                Owned = true,
                Location = "电源盒 B1",
                Substitutes = [],
                Notes = "常用线性稳压器，适合 5V 转 3.3V 低功耗场景。",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "res-10k",
                Name = "10k 电阻",
                PartNumber = "RES-10K-0603-1%",
                Category = "电阻",
                Package = "0603",
                Aliases = ["10k 0603"],
                Keywords = ["pull-up", "divider", "1%"],
                Attributes = new Dictionary<string, string>
                {
                    ["resistance"] = "10k",
                    ["tolerance"] = "1%",
                },
                Owned = true,
                Location = "阻容盒 C4",
                Substitutes = [],
                Notes = "上拉、分压常用，数量较多。",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "conn-usbc",
                Name = "Type-C 16P",
                PartNumber = "USB-C-16P-SMD",
                Category = "连接器",
                Package = "SMD",
                Aliases = ["USB-C 16Pin"],
                Keywords = ["usb2.0", "download", "power"],
                Attributes = new Dictionary<string, string>
                {
                    ["protocol"] = "USB2.0",
                    ["pins"] = "16P",
                },
                Owned = true,
                Location = "连接器盒 D1",
                Substitutes = [],
                Notes = "适合供电和串口下载接口。",
                SourceType = ComponentSourceType.Manual,
            },
        ];
    }
}
