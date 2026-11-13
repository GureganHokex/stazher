"""Integration tests run in-process; no listening socket or external service is needed."""
import importlib.util
import os
import tempfile
import unittest
import uuid
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from fastapi.testclient import TestClient


class ApiTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        os.environ['IGS_DATA'] = self.temp.name
        spec = importlib.util.spec_from_file_location('igs_server', Path(__file__).with_name('server.py'))
        self.server = importlib.util.module_from_spec(spec)
        sys.modules['igs_server'] = self.server
        spec.loader.exec_module(self.server)
        self.client = TestClient(self.server.app)
        self.headers = {'X-IGS-Demo': 'local'}

    def tearDown(self):
        self.client.close()
        self.temp.cleanup()
        os.environ.pop('IGS_DATA', None)

    def post(self, path, body):
        return self.client.post(path, json=body, headers=self.headers)

    def event(self, kind='impression', seconds=0):
        return dict(eventId=str(uuid.uuid4()), sessionId=str(uuid.uuid4()), placementId='office_coffee',
                    creativeId='igs', kind=kind, seconds=seconds, timestamp='2026-11-13T14:00:00Z')

    def test_assignment_and_disabled_campaign_reach_game(self):
        self.assertEqual(len(self.client.get('/api/config').json()['placements']), 3)
        self.assertEqual(self.post('/api/assign', {'placementId': 'office_coffee', 'creativeId': 'igs'}).status_code, 200)
        self.post('/api/enabled', {'creativeId': 'igs', 'enabled': False})
        row = next(p for p in self.client.get('/api/config').json()['placements'] if p['placementId'] == 'office_coffee')
        self.assertEqual(row['creativeId'], 'igs')
        self.assertFalse(row['enabled'])

    def test_retries_do_not_double_count(self):
        event = self.event()
        for _ in range(3):
            response = self.post('/api/events', {'events': [event]})
            self.assertEqual(response.json()['accepted'], [event['eventId']])
        self.post('/api/events', {'events': [self.event('view_time', 3.25)]})
        row = self.client.get('/api/report').json()['rows'][0]
        self.assertEqual(row['impressions'], 1)
        self.assertEqual(row['visible_seconds'], 3.25)

    def test_invalid_batch_rolls_back(self):
        self.assertEqual(self.post('/api/events', {'events': [self.event(), self.event('impression', 2)]}).status_code, 422)
        self.assertEqual(self.client.get('/api/report').json()['rows'], [])

    def test_invalid_duration_rejected(self):
        self.assertEqual(self.post('/api/events', {'events': [self.event('view_time', -1)]}).status_code, 422)

    def test_foreign_browser_cannot_change_campaign(self):
        body = {'placementId': 'office_coffee', 'creativeId': 'igs'}
        self.assertEqual(self.client.post('/api/assign', json=body).status_code, 403)
        self.assertEqual(self.client.post('/api/assign', json=body, headers={**self.headers, 'Origin': 'https://example.com'}).status_code, 403)
        self.assertEqual(self.client.get('/api/config', headers={'Host': 'evil.example'}).status_code, 400)

    def test_upload_rejects_disguised_file(self):
        before = set(self.server.MEDIA.iterdir())
        response = self.client.post('/api/upload?filename=bad.mp4&duration=15', content=b'not a video', headers=self.headers)
        self.assertEqual(response.status_code, 400)
        self.assertEqual(set(self.server.MEDIA.iterdir()), before)

    def test_media_upload_and_serving(self):
        # A file-signature fixture, not a playable video: decoder coverage is a Unity smoke test.
        data = b'\x00\x00\x00\x18ftypisom' + b'\x00' * 32
        response = self.client.post('/api/upload?filename=demo.mp4&title=Demo&duration=15', content=data, headers=self.headers)
        self.assertEqual(response.status_code, 200)
        cid = response.json()['id']
        self.assertEqual(self.client.get('/media/' + cid + '.mp4').content, data)
        self.assertEqual(self.client.get('/media/server.py').status_code, 404)

    def make_vast(self):
        data = b'\x00\x00\x00\x18ftypisom' + b'\x00' * 32
        cid = self.client.post('/api/upload?filename=demo.mp4&duration=15', content=data, headers=self.headers).json()['id']
        self.post('/api/assign', {'placementId': 'office_coffee', 'creativeId': cid})
        response = self.client.get('/vast/office_coffee.xml', params={'session': str(uuid.uuid4()), 'creative': cid})
        self.assertEqual(response.status_code, 200)
        return ET.fromstring(response.content), cid

    def test_vast_returns_mp4_and_loading_does_not_fire_pixels(self):
        xml, cid = self.make_vast()
        media = xml.find('.//MediaFile')
        self.assertEqual(media.attrib['type'], 'video/mp4')
        self.assertTrue(media.text.endswith(cid + '.mp4'))
        self.assertEqual(xml.find('.//Duration').text, '00:00:15.000')
        self.assertEqual(self.client.get('/api/pixels').json()['rows'], [])

    def test_vast_pixels_arrive_and_retries_are_idempotent(self):
        xml, _ = self.make_vast()
        urls = [xml.find('.//Impression').text] + [x.text for x in xml.findall('.//Tracking')]
        for url in urls:
            for _ in range(2):
                response = self.client.get(url.replace('[CACHEBUSTING]', '12345678'), headers=self.headers)
                self.assertEqual(response.status_code, 200)
                self.assertEqual(response.headers['content-type'], 'image/gif')
        rows = self.client.get('/api/pixels').json()['rows']
        self.assertEqual(len(rows), 7)
        self.assertTrue(all(r['hits'] == 1 for r in rows))
        self.assertEqual(self.client.get('/api/report').json()['rows'], [])

    def test_pixels_reject_unknown_ad_and_foreign_page(self):
        self.assertEqual(self.client.get('/pixel/start.gif?ad=' + str(uuid.uuid4()), headers=self.headers).status_code, 404)
        xml, _ = self.make_vast()
        self.assertEqual(self.client.get(xml.find('.//Impression').text).status_code, 403)

    def test_disabled_or_changed_assignment_has_empty_vast(self):
        _, cid = self.make_vast()
        self.post('/api/enabled', {'creativeId': cid, 'enabled': False})
        response = self.client.get('/vast/office_coffee.xml?session=' + str(uuid.uuid4()))
        self.assertEqual(len(ET.fromstring(response.content)), 0)

    def test_csv_and_empty_dashboard(self):
        self.assertEqual(self.client.get('/').status_code, 200)
        response = self.client.get('/api/export.csv')
        self.assertIn('visible_seconds', response.text)


if __name__ == '__main__':
    unittest.main()
