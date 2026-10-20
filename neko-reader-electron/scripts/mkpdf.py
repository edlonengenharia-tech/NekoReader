# gerador minimalista de samples/exemplo.pdf (3 paginas) - sem bucle
import os
objs = []
objs.append(b'1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n')
objs.append(b'2 0 obj<</Type/Pages/Kids[3 0 R 6 0 R 9 0 R]/Count 3>>endobj\n')
objs.append(b'3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n')
objs.append(b'4 0 obj<</Length 46>>stream\nBT /F1 48 Tf 100 600 Td (Pagina 1) Tj ET\nendstream endobj\n')
objs.append(b'5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n')
objs.append(b'6 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 7 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n')
objs.append(b'7 0 obj<</Length 46>>stream\nBT /F1 48 Tf 100 600 Td (Pagina 2) Tj ET\nendstream endobj\n')
objs.append(b'8 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n')
objs.append(b'9 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 10 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n')
objs.append(b'10 0 obj<</Length 46>>stream\nBT /F1 48 Tf 100 600 Td (Pagina 3) Tj ET\nendstream endobj\n')
objs.append(b'11 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n')

body = b'%PDF-1.4\n'
offs = []
for o in objs:
    offs.append(len(body))
    body += o
xref_pos = len(body)
xref = b'xref\n0 12\n0000000000 65535 f \n'
for off in offs:
    xref += ('%010d 00000 n \n' % off).encode()
trailer = ('trailer<</Size 12/Root 1 0 R>>\nstartxref\n%d\n%%%%EOF\n' % xref_pos).encode()
dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'samples', 'exemplo.pdf')
with open(dst, 'wb') as f:
    f.write(body + xref + trailer)
print('[OK] exemplo.pdf', len(body + xref + trailer), 'bytes')
