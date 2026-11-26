for (const form of document.querySelectorAll("[data-otp-form]")) {
    form.addEventListener("submit", event => {
        if (form.dataset.submitting) {
            event.preventDefault();
            return;
        }
        form.dataset.submitting = "true";
        form.setAttribute("aria-busy", "true");
        const button = form.querySelector("button[type=submit]");
        button.disabled = true;
        button.textContent = "در حال پردازش…";
    });
}

const resend = document.querySelector("[data-otp-countdown]");
if (resend && Number(resend.dataset.otpCountdown) > 0) {
    const label = resend.textContent.trim();
    const deadline = Date.now() + Number(resend.dataset.otpCountdown) * 1000;
    const persian = new Intl.NumberFormat("fa-IR");
    const tick = () => {
        const seconds = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        resend.disabled = seconds > 0;
        resend.textContent = seconds > 0 ? `${label} (${persian.format(seconds)})` : label;
        if (seconds > 0) setTimeout(tick, 250);
    };
    tick();
}
