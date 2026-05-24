#include "main.hpp"
#include "scotland2/shared/modloader.h"

#include "RegressionMod/DummyTarget.hpp"

static modloader::ModInfo modInfo{"com.example.regressionmod", "1.0.0", 0};

Configuration &getConfig() {
    static Configuration config(modInfo);
    return config;
}

static int32_t RegressionMod_RegressionHooks_UseSwitchAndLoops_System_Int32(int32_t input);
static void RegressionMod_RegressionHooks_Increment_System_Int32_(int32_t& value);
static int32_t RegressionMod_RegressionHooks_SumSeededArray_System_Int32(int32_t seed);

static int32_t RegressionMod_RegressionHooks_UseSwitchAndLoops_System_Int32(int32_t input) {
    int32_t total{};
    int32_t index{};
    int32_t local2{};
    int32_t local3{};

    int32_t local5{};
    
    total = 0;
    index = 0;
    label_5:;
    local3 = ((input + index) % 4);
    local2 = local3;
    switch (local2) {
        case 0: goto label_17;
        case 1: goto label_22;
        case 2: goto label_27;
        default: break;
    }
    goto label_32;
    label_17:;
    total = (total + index);
    goto label_37;
    label_22:;
    index = (index + 1);
    goto label_42;
    label_27:;
    total = (total + input);
    goto label_37;
    label_32:;
    total = (total - 1);
    goto label_37;
    label_37:;
    index = (index + 1);
    label_42:;
    if ((index < 5)) goto label_5;
    local5 = total;
    goto label_51;
    label_51:;
    return local5;
}

static void RegressionMod_RegressionHooks_Increment_System_Int32_(int32_t& value) {
    value = (value + 2);
}

static int32_t RegressionMod_RegressionHooks_SumSeededArray_System_Int32(int32_t seed) {
    ArrayW<int32_t> values{};
    int32_t total{};
    int32_t i{};

    ArrayW<int32_t> local4{};
    int32_t local5{};
    int32_t value{};
    int32_t local7{};
    
    values = ArrayW<int32_t>(3);
    i = 0;
    goto label_17;
    label_7:;
    values[i] = (seed + i);
    i = (i + 1);
    label_17:;
    if ((i < static_cast<int32_t>(static_cast<int>(values.size())))) goto label_7;
    total = 0;
    local4 = values;
    local5 = 0;
    goto label_45;
    label_33:;
    value = local4[local5];
    total = (total + value);
    local5 = (local5 + 1);
    label_45:;
    if ((local5 < static_cast<int32_t>(static_cast<int>(local4.size())))) goto label_33;
    local7 = total;
    goto label_53;
    label_53:;
    return local7;
}

MAKE_HOOK_MATCH(
    RegressionMod_DummyTarget_Compute_RegressionMod_DummyTarget_System_Int32_Hook,
    &RegressionMod::DummyTarget::Compute,
    int32_t,
    RegressionMod::DummyTarget* self, int32_t input) {
    int32_t result{};

    result = RegressionMod_RegressionHooks_UseSwitchAndLoops_System_Int32(input);
    RegressionMod_RegressionHooks_Increment_System_Int32_(result);
    return (result + RegressionMod_RegressionHooks_SumSeededArray_System_Int32(result));
}

MOD_EXTERN_FUNC void late_load() noexcept {
    il2cpp_functions::Init();
    PaperLogger.info("Installing hooks...");

    INSTALL_HOOK(PaperLogger, RegressionMod_DummyTarget_Compute_RegressionMod_DummyTarget_System_Int32_Hook);

    PaperLogger.info("Installed all hooks!");
}
