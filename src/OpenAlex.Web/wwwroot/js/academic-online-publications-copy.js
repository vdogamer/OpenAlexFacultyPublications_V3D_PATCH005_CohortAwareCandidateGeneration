(() => {
    "use strict";

    const profileButton = document.getElementById("copyProfileReadyPublications");
    const htmlButton = document.getElementById("copyPublicationHtmlSource");
    const status = document.getElementById("publicationCopyStatus");
    const renderedRoot = document.getElementById("generatedPublicationProfileContent");
    const htmlSource = document.getElementById("generatedPublicationHtmlSource");

    if (!profileButton || !htmlButton || !status || !renderedRoot || !htmlSource) {
        return;
    }

    const setStatus = (message, isError = false) => {
        status.textContent = message;
        status.setAttribute("data-copy-status", isError ? "error" : "success");
    };

    const fallbackCopyText = (text) => {
        const textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.setAttribute("readonly", "");
        textarea.style.position = "fixed";
        textarea.style.left = "-9999px";
        textarea.style.top = "0";
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();

        const copied = document.execCommand("copy");
        document.body.removeChild(textarea);

        if (!copied) {
            throw new Error("The browser did not allow clipboard access.");
        }
    };

    const copyText = async (text) => {
        if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
            await navigator.clipboard.writeText(text);
            return;
        }

        fallbackCopyText(text);
    };

    profileButton.addEventListener("click", async () => {
        const publicationBlock =
            renderedRoot.querySelector(".faculty-publications");

        if (!publicationBlock) {
            setStatus("No generated publication block is available to copy.", true);
            return;
        }

        const html = publicationBlock.outerHTML;
        const plainText = publicationBlock.innerText.trim();

        try {
            if (
                navigator.clipboard &&
                typeof navigator.clipboard.write === "function" &&
                typeof window.ClipboardItem === "function"
            ) {
                const clipboardItem = new ClipboardItem({
                    "text/html": new Blob([html], { type: "text/html" }),
                    "text/plain": new Blob([plainText], { type: "text/plain" })
                });

                await navigator.clipboard.write([clipboardItem]);
                setStatus(
                    "Copied formatted publications. Open the faculty profile editor and press Ctrl+V."
                );
                return;
            }

            await copyText(plainText);
            setStatus(
                "Copied publications as plain text because this browser does not support rich clipboard content."
            );
        } catch (error) {
            console.error("Unable to copy formatted publications.", error);
            setStatus(
                "Clipboard access was blocked. Use Copy HTML Source or select the generated publications manually.",
                true
            );
        }
    });

    htmlButton.addEventListener("click", async () => {
        const html = htmlSource.value;

        if (!html.trim()) {
            setStatus("No generated HTML is available to copy.", true);
            return;
        }

        try {
            await copyText(html);
            setStatus(
                "Copied HTML source. Paste it into the profile's Pubs / HTML field with Ctrl+V."
            );
        } catch (error) {
            console.error("Unable to copy publication HTML.", error);
            setStatus(
                "Clipboard access was blocked. Select the HTML source below and copy it manually.",
                true
            );
        }
    });
})();
