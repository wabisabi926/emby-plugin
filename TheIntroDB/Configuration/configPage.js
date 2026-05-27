define(["emby-input", "emby-button", "emby-checkbox"],
    function() {
        const pluginId = "424b8e01-03d2-40a1-ba58-a2b9306f115d";

        function setChecked(view, selector, value, defaultValue) {
            const el = view.querySelector(selector);
            if (!el) return;
            if (value === undefined || value === null) {
                el.checked = defaultValue;
            } else {
                el.checked = !!value;
            }
        }

        function setValue(view, selector, value) {
            const el = view.querySelector(selector);
            if (!el) return;
            el.value = value || "";
        }

        function getValue(view, selector) {
            const el = view.querySelector(selector);
            return el ? el.value : "";
        }

        function getChecked(view, selector) {
            const el = view.querySelector(selector);
            return el ? !!el.checked : false;
        }

        function loadConfig(view) {
            Dashboard.showLoadingMsg();
            return ApiClient.getPluginConfiguration(pluginId).then(function(config) {
                setValue(view, "#ApiKey", config.ApiKey);
                setChecked(view, "#EnableIntro", config.EnableIntro, true);
                setChecked(view, "#EnableRecap", config.EnableRecap, true);
                setChecked(view, "#EnableCredits", config.EnableCredits, true);
                setChecked(view, "#EnablePreview", config.EnablePreview, true);
                setChecked(view, "#IgnoreMediaWithExistingSegments", config.IgnoreMediaWithExistingSegments, true);
                setChecked(view, "#EnableAnonymousUsageReporting", config.EnableAnonymousUsageReporting, true);
                Dashboard.hideLoadingMsg();
            }).catch(function() {
                Dashboard.hideLoadingMsg();
            });
        }

        function saveConfig(view) {
            Dashboard.showLoadingMsg();
            return ApiClient.getPluginConfiguration(pluginId).then(function(config) {
                config.ApiKey = getValue(view, "#ApiKey");
                config.EnableIntro = getChecked(view, "#EnableIntro");
                config.EnableRecap = getChecked(view, "#EnableRecap");
                config.EnableCredits = getChecked(view, "#EnableCredits");
                config.EnablePreview = getChecked(view, "#EnablePreview");
                config.IgnoreMediaWithExistingSegments = getChecked(view, "#IgnoreMediaWithExistingSegments");
                config.EnableAnonymousUsageReporting = getChecked(view, "#EnableAnonymousUsageReporting");

                return ApiClient.updatePluginConfiguration(pluginId, config).then(function(result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                    Dashboard.hideLoadingMsg();
                });
            }).catch(function() {
                Dashboard.hideLoadingMsg();
            });
        }

        function loadSegments(view) {
            const output = view.querySelector(".segmentOutput");
            if (!output) return;
            output.textContent = "";

            const idRaw = getValue(view, "#DebugInternalId");
            const internalId = parseInt(idRaw, 10);
            if (!internalId) {
                output.textContent = "Enter a valid InternalId.";
                return;
            }

            Dashboard.showLoadingMsg();
            ApiClient.getJSON(ApiClient.getUrl("TheIntroDB/Segments?InternalId=" + internalId))
                .then(function(result) {
                    output.textContent = JSON.stringify(result, null, 2);
                    Dashboard.hideLoadingMsg();
                })
                .catch(function(err) {
                    output.textContent = (err && err.message) ? err.message : "Failed to load segments.";
                    Dashboard.hideLoadingMsg();
                });
        }

        return function(view) {
            view.addEventListener("viewshow", function() {
                loadConfig(view);
            });

            const form = view.querySelector("#TheIntroDbConfigForm");
            if (form) {
                form.addEventListener("submit", function(e) {
                    e.preventDefault();
                    saveConfig(view);
                    return false;
                });
            }

            const btn = view.querySelector(".btnLoadSegments");
            if (btn) {
                btn.addEventListener("click", function(e) {
                    e.preventDefault();
                    loadSegments(view);
                });
            }
        };
    });
