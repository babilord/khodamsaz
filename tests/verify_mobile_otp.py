"""Check the OTP UI against a disposable Development app using the fake sender."""
import os
import sys

from playwright.sync_api import expect, sync_playwright

root = sys.argv[1].rstrip("/")

with sync_playwright() as playwright:
    browser = playwright.chromium.launch(channel=os.getenv("BROWSER_CHANNEL", "msedge"))
    for width, number in [(320, "۰۹۱۲۳۴۵۶۷۸۹"), (390, "+989223456789"), (1440, "989323456789")]:
        page = browser.new_page(viewport={"width": width, "height": 820})
        assert page.goto(root + "/Account/Login").ok
        page.locator('a[href*="/account/mobile/Login"]').click()
        expect(page.locator("html")).to_have_attribute("dir", "rtl")
        expect(page.locator("h1")).to_have_text("ورود با شماره موبایل")
        page.locator("#otp-phone").fill(number)
        page.locator("[data-otp-countdown]").click()
        code = page.locator("#otp-code")
        expect(code).to_have_attribute("inputmode", "numeric")
        expect(code).to_have_attribute("autocomplete", "one-time-code")
        code.fill("۱۲۳۴۵۶")
        expect(code).to_have_value("۱۲۳۴۵۶")
        expect(page.locator("[data-otp-countdown]")).to_be_disabled()
        assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
        page.close()
    browser.close()

print("OTP RTL form, keyboard, code entry and resend countdown passed at three viewport widths.")
