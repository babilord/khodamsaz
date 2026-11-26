(() => {
    const dialog = document.getElementById('purchase-declaration-modal');
    const forms = document.querySelectorAll('[data-purchase-form]');
    if (!dialog || !forms.length) return;

    const modal = bootstrap.Modal.getOrCreateInstance(dialog);
    const confirm = dialog.querySelector('[data-purchase-confirm]');
    const cancel = dialog.querySelector('[data-bs-dismiss]');
    const error = dialog.querySelector('[data-purchase-error]');
    const format = new Intl.NumberFormat('fa-IR').format;
    const requestIds = new Map();
    let activeForm;
    let pending = false;

    function showError(message) {
        error.textContent = message;
        error.hidden = !message;
    }

    for (const form of forms) {
        const trigger = form.querySelector('[data-purchase-open]');
        form.addEventListener('submit', event => event.preventDefault());
        trigger.addEventListener('click', () => {
            activeForm = form;
            showError('');
            modal.show(trigger);
        });
    }

    dialog.addEventListener('hide.bs.modal', event => {
        if (pending) event.preventDefault();
    });
    dialog.addEventListener('hidden.bs.modal', () => {
        const trigger = activeForm?.querySelector('[data-purchase-open]');
        if (trigger?.disabled) {
            const allowance = activeForm.parentElement.querySelector('[data-purchase-allowance]');
            allowance.tabIndex = -1;
            allowance.focus();
        } else {
            trigger?.focus();
        }
        activeForm = undefined;
    });

    confirm.addEventListener('click', async () => {
        if (!activeForm || pending) return;
        const form = activeForm;
        const data = new FormData(form);
        const productId = data.get('productId');

        pending = confirm.disabled = cancel.disabled = true;
        showError('');
        let close = false;

        try {
            // Reuse the operation ID when a response is lost or a request is retried.
            if (!requestIds.has(productId)) requestIds.set(productId, crypto.randomUUID());
            data.set('requestId', requestIds.get(productId));
            const response = await fetch(form.action, { method: 'POST', body: data, redirect: 'manual' });
            if (response.type === 'opaqueredirect' || response.status === 401) {
                window.location.assign(form.dataset.loginUrl);
                return;
            }
            const result = response.headers.get('content-type')?.includes('application/json')
                ? await response.json() : null;
            if (result && (response.ok || response.status === 409)) {
                for (const section of document.querySelectorAll(`[data-purchase-product="${productId}"]`)) {
                    section.querySelector('[data-purchase-stats]').textContent =
                        `${format(result.uniqueBuyers)} نفر، ${format(result.total)} بار خرید این محصول را اعلام کرده‌اند.`;
                    section.querySelector('[data-purchase-open]').disabled = result.remaining === 0;
                    section.querySelector('[data-purchase-allowance]').textContent = result.remaining === 0
                        ? 'شما حداکثر ۱۰ بار خرید این محصول را ثبت کرده‌اید.'
                        : `${format(result.remaining)} بار امکان ثبت خرید دارید.`;
                    const success = section.querySelector('[data-purchase-success]');
                    success.textContent = response.ok ? result.message : '';
                    success.hidden = !response.ok;
                }
                requestIds.delete(productId);
                close = true;
            } else {
                showError(result?.message ?? 'ثبت خرید انجام نشد. دوباره تلاش کنید.');
            }
        } catch {
            showError('ارتباط برقرار نشد. دوباره تلاش کنید.');
        } finally {
            pending = confirm.disabled = cancel.disabled = false;
            if (close) modal.hide();
        }
    });
})();
