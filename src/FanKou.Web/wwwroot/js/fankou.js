// 手牌区的排版要按"容器实际宽多少"来算重叠量，浏览器里只能用 ResizeObserver 量回来。
window.fankou = {
    observeSize: function (element, dotnetRef) {
        if (!element) {
            return null;
        }

        const report = () => {
            try {
                dotnetRef.invokeMethodAsync('OnResize', element.clientWidth, element.clientHeight);
            } catch (e) {
                // 组件已经销毁了，忽略。
            }
        };

        const observer = new ResizeObserver(report);
        observer.observe(element);
        report();

        return {
            dispose: function () {
                try { observer.disconnect(); } catch (e) { }
            }
        };
    },

    // 键盘：回车出牌、空格提示、Ctrl+Z 撤回。挂在 document 上，省得管焦点。
    attachKeys: function (dotnetRef) {
        if (window.__fankouKeyHandler) {
            document.removeEventListener('keydown', window.__fankouKeyHandler);
        }

        window.__fankouKeyHandler = (e) => {
            const tag = e.target && e.target.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
                return;
            }

            try {
                dotnetRef.invokeMethodAsync('OnKeyDown', e.key, e.ctrlKey || e.metaKey);
            } catch (err) {
                // 忽略
            }
        };

        document.addEventListener('keydown', window.__fankouKeyHandler);
    },

    detachKeys: function () {
        if (window.__fankouKeyHandler) {
            document.removeEventListener('keydown', window.__fankouKeyHandler);
            window.__fankouKeyHandler = null;
        }
    }
};
