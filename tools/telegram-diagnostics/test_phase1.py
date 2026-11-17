import json
import unittest
from pathlib import Path
from audit_raw_telegram import audit

ROOT = Path(__file__).resolve().parent

class Phase1Tests(unittest.TestCase):
    def test_fixtures_are_unique_and_retain_raw_text(self):
        data = json.loads((ROOT / "raw_message_cases.json").read_text(encoding="utf-8"))
        cases = data["cases"]
        self.assertGreaterEqual(len(cases), 20)
        self.assertEqual(len({case["id"] for case in cases}), len(cases))
        self.assertTrue(all(case["rawMessage"] and case["source"]["channel"] for case in cases))

    def test_audit_keeps_unmatched_and_empty_messages(self):
        sample = {"results": {"rawTelegramMessages": [
            {"channel": "a", "rawMessage": ""},
            {"channel": "a", "rawMessage": "متن تبلیغ بدون خودرو"},
            {"channel": "b", "rawMessage": "۲۰۷ مدل ۴۰۵"}],
            "parsedAdvertisements": [{"name": "۲۰۷"}]}}
        result = audit(sample)
        self.assertEqual(result["rawMessageCount"], 3)
        self.assertEqual(result["emptyRawMessages"], 1)
        self.assertEqual(result["parsedAdvertisementsCount"], 1)

if __name__ == "__main__":
    unittest.main()
