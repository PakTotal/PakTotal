window.pakTotal = {
    savedTheme() {
        try {
            return localStorage.getItem("paktotal-theme") === "light" ? "light" : "dark";
        } catch {
            return "dark";
        }
    },

    applyTheme(theme) {
        if (theme === "dark") document.documentElement.setAttribute("data-theme", "dark");
        else document.documentElement.removeAttribute("data-theme");
    },

    toggleTheme() {
        const theme = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
        pakTotal.applyTheme(theme);
        try {
            localStorage.setItem("paktotal-theme", theme);
        } catch {
        }
    },

    async fileNames(input) {
        if (!input || !input.files) return [];
        const names = [];
        for (const file of input.files) {
            if (file.name.toLowerCase().endsWith(".zip")) {
                try {
                    names.push(...await pakTotal.zipEntryNames(file));
                } catch {
                    names.push(file.name);
                }
            } else {
                names.push(file.name);
            }
        }
        return names;
    },

    async zipEntryNames(file) {
        const tailSize = Math.min(file.size, 65557);
        const tail = new DataView(await file.slice(file.size - tailSize).arrayBuffer());
        let eocd = -1;
        for (let i = tail.byteLength - 22; i >= 0; i--) {
            if (tail.getUint32(i, true) === 0x06054b50) {
                eocd = i;
                break;
            }
        }
        if (eocd < 0) throw new Error("not a zip");

        let count = tail.getUint16(eocd + 10, true);
        let size = tail.getUint32(eocd + 12, true);
        let offset = tail.getUint32(eocd + 16, true);
        if (offset === 0xffffffff || size === 0xffffffff) {
            const locator = eocd - 20;
            if (locator < 0 || tail.getUint32(locator, true) !== 0x07064b50) throw new Error("zip64 locator missing");
            const zip64Offset = Number(tail.getBigUint64(locator + 8, true));
            const zip64 = new DataView(await file.slice(zip64Offset, zip64Offset + 56).arrayBuffer());
            count = Number(zip64.getBigUint64(32, true));
            size = Number(zip64.getBigUint64(40, true));
            offset = Number(zip64.getBigUint64(48, true));
        }

        const directory = new DataView(await file.slice(offset, offset + size).arrayBuffer());
        const decoder = new TextDecoder();
        const names = [];
        let position = 0;
        for (let i = 0; i < count && position + 46 <= directory.byteLength; i++) {
            if (directory.getUint32(position, true) !== 0x02014b50) break;
            const nameLength = directory.getUint16(position + 28, true);
            const extraLength = directory.getUint16(position + 30, true);
            const commentLength = directory.getUint16(position + 32, true);
            const name = decoder.decode(new Uint8Array(directory.buffer, directory.byteOffset + position + 46, nameLength));
            const baseName = name.split("/").pop();
            if (baseName) names.push(baseName);
            position += 46 + nameLength + extraLength + commentLength;
        }
        return names;
    },

    selectedNames(input) {
        return input && input.files ? Array.from(input.files, file => file.name) : [];
    },

    async submitScan(archiveInput, globalInput, mappingsInput, aesKey, engineVersion) {
        const form = new FormData();
        for (const file of archiveInput.files) form.append("archive", file, file.name);
        if (globalInput) {
            for (const file of globalInput.files) form.append("global", file, file.name);
        }
        if (mappingsInput && mappingsInput.files.length > 0) form.append("mappings", mappingsInput.files[0], mappingsInput.files[0].name);
        if (aesKey) form.append("aesKey", aesKey);
        form.append("engineVersion", engineVersion || "");
        return await pakTotal.request("api/v1/scans", { method: "POST", body: form });
    },

    replacePath(path) {
        history.replaceState(history.state, "", new URL(path, document.baseURI));
    },

    async getEngineVersions() {
        return await pakTotal.request("api/v1/engine-versions", { method: "GET" });
    },

    async getScan(id) {
        return await pakTotal.request(`api/v1/scans/${encodeURIComponent(id)}`, { method: "GET" });
    },

    async request(url, init) {
        try {
            const response = await fetch(url, init);
            let body = null;
            try {
                body = await response.json();
            } catch {
                body = null;
            }
            return { status: response.status, ok: response.ok, body: body, error: body && body.error ? body.error : null };
        } catch (e) {
            return { status: 0, ok: false, body: null, error: "Could not reach the server." };
        }
    }
};

pakTotal.tips = [
    "Undetected doesn't mean safe! It means none of the modules found anything they look for.",
    "Only install mods from authors you trust, whatever the scan says.",
    "IoStore mods come as .utoc + .ucas + .pak",
    "Most games need mappings. A .jmap or .usmap lets me read the blueprints inside.",
    "Encrypted pak? Pop the AES key in and I can look inside.",
    "Same files, same hash. If someone scanned it already, you get the answer straight away.",
    "Back up your save files before trying out new mods.",
    "Found something nasty? Report it to the mod site so they can take it down.",
    "Who's a good little Seal?",
];

pakTotal.speak = function () {
    const bubble = document.querySelector("[data-mascot-speech]");
    if (!bubble) return;
    let tip;
    do {
        tip = pakTotal.tips[Math.floor(Math.random() * pakTotal.tips.length)];
    } while (pakTotal.tips.length > 1 && tip === bubble.textContent);
    bubble.textContent = tip;
    bubble.hidden = false;
    clearTimeout(pakTotal.speechTimer);
    pakTotal.speechTimer = setTimeout(() => bubble.hidden = true, 7000);
};

document.addEventListener("click", event => {
    if (event.target.closest("[data-theme-toggle]")) pakTotal.toggleTheme();
    if (event.target.closest("[data-mascot]")) pakTotal.speak();
});

document.addEventListener("DOMContentLoaded", () => {
    window.Blazor?.addEventListener?.("enhancedload", () => pakTotal.applyTheme(pakTotal.savedTheme()));
});
