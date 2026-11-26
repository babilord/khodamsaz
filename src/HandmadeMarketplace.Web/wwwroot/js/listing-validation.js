(() => {
    // MVC can emit a native maxlength alongside its unobtrusive length rule.
    for (const field of document.querySelectorAll("[data-val-length]")) {
        field.setAttribute("data-msg-maxlength", field.getAttribute("data-val-length"));
    }

    const range = $.validator.methods.range;
    $.validator.methods.range = function (value, element, limits) {
        if (element.matches("[data-price]")) {
            value = value.trim()
                .replace(/[۰-۹]/g, digit => "۰۱۲۳۴۵۶۷۸۹".indexOf(digit))
                .replace(/[٠-٩]/g, digit => "٠١٢٣٤٥٦٧٨٩".indexOf(digit))
                .replace(/[,٬]/g, "");
            if (!/^\d+$/.test(value)) return false;
        }
        return range.call(this, value, element, limits);
    };
})();
