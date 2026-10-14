# A plugin asks for its capabilities

A third-party plugin lists the capabilities it wants. The host binds those families and still attaches the plugin when a family is not implemented. `power` is the battery. `power.session` sleeps, restarts, or shuts down the Host, and only a clone of `winoma.power` receives it. A clone that asks for both also receives the family the built-in binds, which adds the power modes, so the copied plugin runs as it is. Brightness is not in that family: `display` is the monitors and the brightness, and the power plugin does not bind it. `menu` and `update` go only to a clone of their built-in too.

The families are separate interfaces, and `PluginBridge` is the adapter for them, except the indicator roster, which `PluginRuntime` holds because plugins publish into it (ADR 0031). One shared interface would let a clock replacement sleep the Host, or would turn the grant into a check the compiler no longer makes.

The manifest id is the key. The class `Id` must equal it, ordinal and case-sensitive, and neither may be empty. `PluginGrants.For` maps a built-in id to its family. An id it does not know still attaches, with the families it asked for and no other.

The `winoma.` prefix is reserved. A folder under the user plugins directory does not replace a built-in. That directory is `%USERPROFILE%\.config\winshell\plugins`. Built-ins stay in `plugins` beside the program. `clonedFrom` routes that built-in id to the clone, and the built-in slot keeps its formats. A built-in that binds a family is skipped, with one information line, when the shell has no services for it. A sheet still starts.

A widget's values live on its bar slot. `barWidget.schema` names each key as `string`, `number`, or `bool`. `winoma bar set` writes one value, and the plugin reads it from its slot.
