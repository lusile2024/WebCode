window.authHelper = {
    async login(username, password) {
        const response = await fetch('/api/auth/login', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            credentials: 'same-origin',
            body: JSON.stringify({ username, password })
        });

        const data = await response.json();
        if (response.ok && data.isAuthenticated) {
            this.persistSession(data);
        } else {
            this.clearSession();
        }

        return data;
    },

    async logout() {
        try {
            await fetch('/api/auth/logout', {
                method: 'POST',
                credentials: 'same-origin'
            });
        } finally {
            this.clearSession();
        }
    },

    async getCurrentUser() {
        const response = await fetch('/api/auth/me', {
            method: 'GET',
            credentials: 'same-origin'
        });

        const data = await response.json();
        if (response.ok && data.isAuthenticated) {
            this.persistSession(data);
        } else {
            this.clearSession();
        }

        return data;
    },

    persistSession(data) {
        sessionStorage.setItem('isAuthenticated', data.isAuthenticated ? 'true' : 'false');
        if (data.username) {
            sessionStorage.setItem('username', data.username);
        }
        if (data.displayName) {
            sessionStorage.setItem('displayName', data.displayName);
        }
        if (data.role) {
            sessionStorage.setItem('userRole', data.role);
        }
    },

    clearSession() {
        sessionStorage.removeItem('isAuthenticated');
        sessionStorage.removeItem('username');
        sessionStorage.removeItem('displayName');
        sessionStorage.removeItem('userRole');
    }
};
