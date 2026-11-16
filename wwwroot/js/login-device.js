(() => {
    document.querySelectorAll('[data-login-device]').forEach(container => {
        const field = name => container.querySelector(`[data-device-field="${name}"]`);
        const status = container.querySelector('[data-device-status]');
        const form = container.closest('form');

        try {
            field('timezone').value = Intl.DateTimeFormat().resolvedOptions().timeZone || '';
        } catch {
            field('timezone').value = '';
        }

        // Discard coordinates from an earlier page render before requesting a fresh location.
        ['latitude', 'longitude', 'accuracy', 'captured'].forEach(name => {
            field(name).value = '';
        });

        if (!window.isSecureContext || !navigator.geolocation) {
            field('status').value = 'Unavailable';
            status.textContent = 'Location needs HTTPS and a supported browser. You can still log in.';
            return;
        }

        let finished = false;
        let complete;
        const locationReady = new Promise(resolve => { complete = resolve; });
        const finish = (value, message) => {
            finished = true;
            field('status').value = value;
            status.textContent = message;
            complete();
        };
        const failed = error => {
            if (finished) return;
            if (error?.code === 1) {
                finish('Denied', 'Location permission was denied. You can still log in. To allow it later, use your browser site settings.');
            } else if (error?.code === 3) {
                finish('Timeout', 'Location request timed out. You can still log in.');
            } else {
                finish('Unavailable', 'Location is unavailable. You can still log in.');
            }
        };

        field('status').value = 'Pending';
        status.textContent = 'Allow or block location in your browser permission popup. Sharing is optional.';

        // Give an in-flight request a bounded chance to finish before posting login metadata.
        let submitting = false;
        form?.addEventListener('submit', async event => {
            if (finished) return;
            event.preventDefault();
            if (submitting) return;
            submitting = true;
            const timeout = setTimeout(() => failed({ code: 3 }), 8000);
            await locationReady;
            clearTimeout(timeout);
            submitting = false;
            if (event.submitter) {
                form.requestSubmit(event.submitter);
            } else {
                form.requestSubmit();
            }
        });

        try {
            navigator.geolocation.getCurrentPosition(position => {
                if (finished) return;
                const { latitude, longitude, accuracy } = position.coords;
                if (![latitude, longitude, accuracy, position.timestamp].every(Number.isFinite) ||
                    latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180 || accuracy < 0) {
                    failed();
                    return;
                }

                field('latitude').value = String(latitude);
                field('longitude').value = String(longitude);
                field('accuracy').value = String(accuracy);
                field('captured').value = String(Math.round(position.timestamp));
                finish('Granted', `Location captured, accuracy about ${Math.round(accuracy)} metres.`);
            }, failed, { enableHighAccuracy: false, timeout: 8000, maximumAge: 60000 });
        } catch {
            failed();
        }
    });
})();
