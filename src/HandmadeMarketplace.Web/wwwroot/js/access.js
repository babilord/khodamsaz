(() => {
    const code = document.querySelector('#access-code');
    if (code) {
        const cells = [...document.querySelectorAll('.access-cells span')];
        const verify = code.form.querySelector('button[type="submit"]');
        const digits = value => value
            .replace(/[۰-۹]/g, digit => String(digit.charCodeAt(0) - 0x06f0))
            .replace(/[٠-٩]/g, digit => String(digit.charCodeAt(0) - 0x0660))
            .replace(/[^0-9]/g, '').slice(0, 6);
        const update = () => {
            code.value = digits(code.value);
            cells.forEach((cell, index) => {
                cell.textContent = code.value[index] ?? '';
                cell.classList.toggle('active', index === Math.min(code.value.length, 5));
            });
            verify.disabled = code.value.length !== 6;
        };
        code.parentElement.classList.add('enhanced');
        code.addEventListener('input', update);
        code.addEventListener('paste', event => {
            const pasted = digits(event.clipboardData.getData('text'));
            if (!pasted) return;
            event.preventDefault();
            const start = code.selectionStart;
            const end = code.selectionEnd;
            code.value = pasted.length === 6 ? pasted : code.value.slice(0, start) + pasted + code.value.slice(end);
            update();
        });
        update();
    }

    const resend = document.querySelector('[data-resend]');
    if (resend) {
        const until = Date.now() + Number(resend.dataset.resend) * 1000;
        const number = new Intl.NumberFormat('fa-IR', { minimumIntegerDigits: 2, useGrouping: false });
        const tick = () => {
            const left = Math.max(0, Math.ceil((until - Date.now()) / 1000));
            resend.disabled = left > 0;
            resend.textContent = left > 0
                ? `ارسال مجدد کد تا ${number.format(Math.floor(left / 60))}:${number.format(left % 60)}`
                : 'ارسال مجدد کد';
            if (left > 0) setTimeout(tick, 250);
        };
        tick();
    }

    const password = document.querySelector('#new-access-password');
    if (password) {
        const confirmation = document.querySelector('[data-password-confirmation]');
        const update = () => {
            confirmation.hidden = !password.value;
            confirmation.querySelector('input').required = Boolean(password.value);
        };
        password.addEventListener('input', update);
        update();
    }

    document.querySelectorAll('[data-access-form]').forEach(form => {
        form.addEventListener('submit', event => {
            if (form.dataset.pending) { event.preventDefault(); return; }
            form.dataset.pending = 'true';
            const button = form.querySelector('[data-busy]');
            if (!button) return;
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            button.textContent = button.dataset.busy;
        });
    });
})();
